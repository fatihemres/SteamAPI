using System.ComponentModel.DataAnnotations;
using Infrastructer.Steam.Enumerations;

namespace SteamAPI.Models
{
    public class GetSteamGameInfoRequestModel
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "The Game field is required and cannot be empty or whitespace.")]
        [MaxLength(200, ErrorMessage = "The Game field cannot exceed 200 characters.")]
        public string Game { get; set; }

        [Required(ErrorMessage = "The Culture field is required.")]
        [EnumDataType(typeof(Culture), ErrorMessage = "The specified Culture value is invalid. Supported values are: tr_TR (1), en_US (2), es_ES (3).")]
        public Culture Culture { get; set; }
    }
}
