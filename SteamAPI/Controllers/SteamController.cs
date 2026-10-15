using Infrastructer.Steam.CustomTypes;
using Infrastructer.Steam.Enumerations;
using Infrastructer.Steam.Services.Abstraction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SteamAPI.Models;

namespace SteamAPI.Controllers
{
    [ApiController]
    [Route("steam")]
    public class SteamController : ControllerBase
    {
        private readonly ISteamService _steamService;

        public SteamController(ISteamService steamService)
        {
            _steamService = steamService ?? throw new ArgumentNullException(nameof(steamService));
        }

        [HttpPost(Name = "steamGameInfo")]
        [ProducesResponseType(typeof(IEnumerable<Listing>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
        public async Task<ActionResult<IEnumerable<Listing>>> GetByCulture([FromBody] GetSteamGameInfoRequestModel request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                ModelState.AddModelError(nameof(request), "Request body cannot be null.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.Game))
                {
                    ModelState.AddModelError(nameof(request.Game), "The Game field is required and cannot be empty or whitespace-only.");
                }

                if (!Enum.IsDefined(typeof(Culture), request.Culture))
                {
                    ModelState.AddModelError(nameof(request.Culture), $"The Culture value '{(int)request.Culture}' is invalid. Supported values are: tr_TR (1), en_US (2), es_ES (3).");
                }
            }

            if (!ModelState.IsValid)
            {
                var validationProblem = new ValidationProblemDetails(ModelState)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred."
                };
                return new BadRequestObjectResult(validationProblem);
            }

            try
            {
                var result = await _steamService.GetListingsAsync(request!.Game, request.Culture, cancellationToken);
                return Ok(result);
            }
            catch (HttpRequestException)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
                {
                    Status = StatusCodes.Status502BadGateway,
                    Title = "Bad Gateway",
                    Detail = "The upstream Steam search service returned an error or was unreachable."
                });
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && ex.InnerException is TimeoutException)
            {
                return StatusCode(StatusCodes.Status504GatewayTimeout, new ProblemDetails
                {
                    Status = StatusCodes.Status504GatewayTimeout,
                    Title = "Gateway Timeout",
                    Detail = "The upstream Steam search service did not respond within the configured timeout."
                });
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
                {
                    Status = StatusCodes.Status502BadGateway,
                    Title = "Bad Gateway",
                    Detail = "The upstream Steam search service returned an error or was unreachable."
                });
            }
        }
    }
}
