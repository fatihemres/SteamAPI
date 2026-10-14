using HtmlAgilityPack;
using Infrastructer.Steam.Enumerations;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Infrastructer.Steam.CustomTypes
{
    public class Listing
    {
        public string Name { get; protected set; } = string.Empty;
        public string StoreLink { get; protected set; } = string.Empty;
        public string ImageLink { get; protected set; } = string.Empty;
        public double? Price { get; protected set; }
        public sType SaleType { get; protected set; } = sType.NotAvailable;
        public string AppId { get; protected set; } = string.Empty;

        internal bool IsValid =>
            !string.IsNullOrWhiteSpace(Name) &&
            !string.IsNullOrWhiteSpace(AppId) &&
            ulong.TryParse(AppId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) &&
            id > 0;

        private static readonly HashSet<string> RecognizedFreeLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            "free",
            "free to play",
            "free-to-play",
            "play for free",
            "ücretsiz",
            "ücretsiz oyna",
            "gratis"
        };

        private enum CurrencyFamily
        {
            Unknown,
            PeriodDecimal, // USD, GBP, CAD, AUD
            Euro,          // EUR, €
            TurkishLira    // TL, ₺, TRY
        }

        public Listing(string listingData, CultureInfo cultureInfo)
        {
            if (string.IsNullOrWhiteSpace(listingData))
            {
                return;
            }

            var doc = new HtmlDocument();
            // Handle both complete <a> elements and legacy fragments starting with attributes
            string html = listingData.TrimStart().StartsWith("<")
                ? listingData
                : $"<a class=\"match\" {listingData}";

            doc.LoadHtml(html);
            var node = doc.DocumentNode.Descendants("a").FirstOrDefault(a => a.HasClass("match"))
                       ?? doc.DocumentNode.FirstChild;

            if (node != null)
            {
                PopulateFromNode(node, cultureInfo);
            }
        }

        public Listing(HtmlNode node, CultureInfo cultureInfo)
        {
            PopulateFromNode(node, cultureInfo);
        }

        private void PopulateFromNode(HtmlNode node, CultureInfo cultureInfo)
        {
            AppId = ExtractAppId(node);
            StoreLink = ExtractStoreLink(node);
            Name = ExtractName(node);
            ImageLink = ExtractImageLink(node);
            var pricing = ExtractPricing(node, cultureInfo);
            SaleType = pricing.SaleType;
            Price = pricing.Price;
        }

        private static string ExtractAppId(HtmlNode node)
        {
            if (node.Attributes.Contains("data-ds-appid"))
            {
                var appId = node.GetAttributeValue("data-ds-appid", string.Empty).Trim();
                if (ulong.TryParse(appId, NumberStyles.None, CultureInfo.InvariantCulture, out var validId) && validId > 0)
                {
                    return appId;
                }

                // If data-ds-appid attribute is explicitly present but non-numeric, it is malformed
                return string.Empty;
            }

            // Fallback from href e.g. /app/730/
            var href = node.GetAttributeValue("href", string.Empty);
            var match = Regex.Match(href, @"/app/(\d+)(?:/|$|\?)", RegexOptions.IgnoreCase);
            if (match.Success && ulong.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hrefId) && hrefId > 0)
            {
                return match.Groups[1].Value;
            }

            // Fallback for legacy raw fragment text where HtmlAgilityPack did not parse attributes onto node
            var outer = node.OuterHtml;
            var attrMatch = Regex.Match(outer, @"data-ds-appid=[""'](\d+)[""']", RegexOptions.IgnoreCase);
            if (attrMatch.Success && ulong.TryParse(attrMatch.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var outerId) && outerId > 0)
            {
                return attrMatch.Groups[1].Value;
            }

            return string.Empty;
        }

        private static string ExtractStoreLink(HtmlNode node)
        {
            var href = node.GetAttributeValue("href", string.Empty);
            if (string.IsNullOrWhiteSpace(href))
            {
                return string.Empty;
            }

            if (href.Contains('?'))
            {
                href = href.Split('?')[0];
            }

            return href.TrimEnd('/');
        }

        private static string ExtractName(HtmlNode node)
        {
            var nameNode = node.Descendants("div").FirstOrDefault(d => d.HasClass("match_name"));
            if (nameNode == null)
            {
                return string.Empty;
            }

            return WebUtility.HtmlDecode(nameNode.InnerText).Trim();
        }

        private static string ExtractImageLink(HtmlNode node)
        {
            var imgNode = node.Descendants("div").FirstOrDefault(d => d.HasClass("match_img"))?.Descendants("img").FirstOrDefault()
                          ?? node.Descendants("img").FirstOrDefault();

            if (imgNode == null)
            {
                return string.Empty;
            }

            var src = imgNode.GetAttributeValue("src", string.Empty);
            if (src.Contains('?'))
            {
                src = src.Split('?')[0];
            }

            return src.Trim();
        }

        private static (sType SaleType, double? Price) ExtractPricing(HtmlNode node, CultureInfo cultureInfo)
        {
            var priceNode = node.Descendants("div").FirstOrDefault(d => d.HasClass("match_price"));
            if (priceNode == null)
            {
                return (sType.NotAvailable, null);
            }

            // Discount handling: prefer final effective price
            var finalPriceNode = priceNode.Descendants().FirstOrDefault(d => d.HasClass("discount_final_price"));
            string rawPriceText;
            if (finalPriceNode != null)
            {
                rawPriceText = finalPriceNode.InnerText;
            }
            else
            {
                // Clone price node to remove struck-through/discount-percentage elements
                var clonedPriceNode = HtmlNode.CreateNode(priceNode.OuterHtml);
                var struckElements = clonedPriceNode.Descendants()
                    .Where(n => n.HasClass("strike") || n.HasClass("discount_pct") || n.HasClass("discount_original_price") || n.Name.Equals("strike", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var el in struckElements)
                {
                    el.Remove();
                }

                rawPriceText = clonedPriceNode.InnerText;
            }

            string decoded = WebUtility.HtmlDecode(rawPriceText).Trim();
            if (string.IsNullOrWhiteSpace(decoded))
            {
                return (sType.NotAvailable, null);
            }

            // Conservative Free-to-Play matching
            if (RecognizedFreeLabels.Contains(decoded))
            {
                return (sType.FreeToPlay, null);
            }

            // Specific price parsing
            var parsedPrice = ParsePrice(decoded, cultureInfo);
            if (parsedPrice.HasValue)
            {
                return (sType.CostsMoney, parsedPrice.Value);
            }

            return (sType.NotAvailable, null);
        }

        public static double? ParsePrice(string priceString, CultureInfo? requestCulture = null)
        {
            if (string.IsNullOrWhiteSpace(priceString))
            {
                return null;
            }

            // Normalize whitespace (including non-breaking spaces)
            string text = priceString.Replace('\u00A0', ' ').Trim();

            // Reject text containing characters that never belong in prices (e.g. %, strikes, etc.)
            if (text.Contains('%') || text.Contains('/') || text.Contains('\\') || text.Contains('#'))
            {
                return null;
            }

            // Disallow arbitrary text words (e.g. "Save", "Coming", "Freedom", "Fighters", "Free", etc.)
            // Only recognized currency codes / abbreviations are permitted as word tokens.
            var words = Regex.Matches(text, @"\b[A-Za-zçğıöşüÇĞİÖŞÜ]+\b");
            foreach (Match w in words)
            {
                string val = w.Value;
                if (!IsAllowedCurrencyWord(val))
                {
                    return null;
                }
            }

            // Determine the active currency family (PeriodDecimal vs Euro vs TurkishLira)
            var currencyFamily = DetectCurrencyFamily(text, requestCulture);
            if (currencyFamily == CurrencyFamily.Unknown)
            {
                // Unrecognized currency representation: reject arbitrary text or numbers without currency
                return null;
            }

            // Strip out all recognized currency symbols and currency word tokens to isolate the numeric part
            string numPart = StripCurrencyIndicators(text);
            if (string.IsNullOrWhiteSpace(numPart))
            {
                return null;
            }

            // Ensure numPart contains ONLY digits and permitted separator characters (period, comma, space)
            if (!Regex.IsMatch(numPart, @"^[\d., ]+$"))
            {
                return null;
            }

            switch (currencyFamily)
            {
                case CurrencyFamily.PeriodDecimal:
                    // USD, GBP, CAD, AUD: decimal separator is '.'
                    // Valid formats:
                    // 1. Integer: ^\d+$ (e.g. "5", "10")
                    // 2. Integer with thousands: ^\d{1,3}(,\d{3})+$
                    // 3. Decimal: ^\d+\.\d{1,2}$ (e.g. "9.99")
                    // 4. Thousands + Decimal: ^\d{1,3}(,\d{3})+\.\d{1,2}$ (e.g. "1,299.99")
                    if (Regex.IsMatch(numPart, @"^\d+$"))
                    {
                        if (double.TryParse(numPart, NumberStyles.None, CultureInfo.InvariantCulture, out double intVal))
                            return intVal;
                    }
                    else if (Regex.IsMatch(numPart, @"^\d{1,3}(,\d{3})+$"))
                    {
                        if (double.TryParse(numPart.Replace(",", ""), NumberStyles.None, CultureInfo.InvariantCulture, out double tVal))
                            return tVal;
                    }
                    else if (Regex.IsMatch(numPart, @"^\d+\.\d{1,2}$"))
                    {
                        if (double.TryParse(numPart, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double decVal))
                            return decVal;
                    }
                    else if (Regex.IsMatch(numPart, @"^\d{1,3}(,\d{3})+\.\d{1,2}$"))
                    {
                        if (double.TryParse(numPart.Replace(",", ""), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double fullVal))
                            return fullVal;
                    }
                    // Any other combination (like "$9,99") is rejected as ambiguous or unsupported
                    return null;

                case CurrencyFamily.TurkishLira:
                    // TL, ₺, TRY: decimal separator is ','
                    // Valid formats:
                    // 1. Integer: ^\d+$ (e.g. "19")
                    // 2. Integer with thousands: ^\d{1,3}(\.\d{3})+$
                    // 3. Decimal: ^\d+,\d{1,2}$ (e.g. "19,99")
                    // 4. Thousands + Decimal: ^\d{1,3}(\.\d{3})+,\d{1,2}$ (e.g. "1.299,99")
                    // 5. Space thousands + Decimal: ^\d{1,3}( \d{3})+,\d{1,2}$
                    if (Regex.IsMatch(numPart, @"^\d+$"))
                    {
                        if (double.TryParse(numPart, NumberStyles.None, CultureInfo.InvariantCulture, out double tlInt))
                            return tlInt;
                    }
                    else if (Regex.IsMatch(numPart, @"^\d{1,3}(\.\d{3})+$"))
                    {
                        if (double.TryParse(numPart.Replace(".", ""), NumberStyles.None, CultureInfo.InvariantCulture, out double tlT))
                            return tlT;
                    }
                    else if (Regex.IsMatch(numPart, @"^\d+,\d{1,2}$"))
                    {
                        if (double.TryParse(numPart.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double tlDec))
                            return tlDec;
                    }
                    else if (Regex.IsMatch(numPart, @"^\d{1,3}(\.\d{3})+,\d{1,2}$"))
                    {
                        string clean = numPart.Replace(".", "").Replace(',', '.');
                        if (double.TryParse(clean, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double tlFull))
                            return tlFull;
                    }
                    else if (Regex.IsMatch(numPart, @"^\d{1,3}( \d{3})+,\d{1,2}$"))
                    {
                        string clean = numPart.Replace(" ", "").Replace(',', '.');
                        if (double.TryParse(clean, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double tlSp))
                            return tlSp;
                    }
                    return null;

                case CurrencyFamily.Euro:
                    // Euro: supports Continental (comma decimal) and Anglo-Euro (period decimal with comma thousands)
                    // 1. Integer: ^\d+$ (e.g. "9")
                    if (Regex.IsMatch(numPart, @"^\d+$"))
                    {
                        if (double.TryParse(numPart, NumberStyles.None, CultureInfo.InvariantCulture, out double eurInt))
                            return eurInt;
                    }
                    // 2. Continental Decimal: ^\d+,\d{1,2}$ (e.g. "9,99")
                    else if (Regex.IsMatch(numPart, @"^\d+,\d{1,2}$"))
                    {
                        if (double.TryParse(numPart.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double eurDec))
                            return eurDec;
                    }
                    // 3. Continental Thousands + Decimal: ^\d{1,3}(\.\d{3})+,\d{1,2}$ (e.g. "1.299,99")
                    else if (Regex.IsMatch(numPart, @"^\d{1,3}(\.\d{3})+,\d{1,2}$"))
                    {
                        string clean = numPart.Replace(".", "").Replace(',', '.');
                        if (double.TryParse(clean, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double eurFull))
                            return eurFull;
                    }
                    // 4. Continental Space Thousands + Decimal: ^\d{1,3}( \d{3})+,\d{1,2}$
                    else if (Regex.IsMatch(numPart, @"^\d{1,3}( \d{3})+,\d{1,2}$"))
                    {
                        string clean = numPart.Replace(" ", "").Replace(',', '.');
                        if (double.TryParse(clean, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double eurSp))
                            return eurSp;
                    }
                    // 5. Continental Integer with Thousands: ^\d{1,3}(\.\d{3})+$
                    else if (Regex.IsMatch(numPart, @"^\d{1,3}(\.\d{3})+$"))
                    {
                        if (double.TryParse(numPart.Replace(".", ""), NumberStyles.None, CultureInfo.InvariantCulture, out double eurT))
                            return eurT;
                    }
                    // 6. Anglo-Euro Decimal: ^\d+\.\d{1,2}$ (e.g. "9.99")
                    else if (Regex.IsMatch(numPart, @"^\d+\.\d{1,2}$"))
                    {
                        if (double.TryParse(numPart, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double eurADec))
                            return eurADec;
                    }
                    // 7. Anglo-Euro Thousands + Decimal: ^\d{1,3}(,\d{3})+\.\d{1,2}$ (e.g. "1,234.56")
                    else if (Regex.IsMatch(numPart, @"^\d{1,3}(,\d{3})+\.\d{1,2}$"))
                    {
                        string clean = numPart.Replace(",", "");
                        if (double.TryParse(clean, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double eurAFull))
                            return eurAFull;
                    }
                    return null;

                default:
                    return null;
            }
        }

        private static bool IsAllowedCurrencyWord(string word)
        {
            return word.Equals("USD", StringComparison.OrdinalIgnoreCase) ||
                   word.Equals("EUR", StringComparison.OrdinalIgnoreCase) ||
                   word.Equals("GBP", StringComparison.OrdinalIgnoreCase) ||
                   word.Equals("TL", StringComparison.OrdinalIgnoreCase) ||
                   word.Equals("TRY", StringComparison.OrdinalIgnoreCase) ||
                   word.Equals("CAD", StringComparison.OrdinalIgnoreCase) ||
                   word.Equals("AUD", StringComparison.OrdinalIgnoreCase);
        }

        private static CurrencyFamily DetectCurrencyFamily(string text, CultureInfo? requestCulture)
        {
            // Explicit currency in text takes precedence
            if (text.Contains('$') ||
                text.Contains("USD", StringComparison.OrdinalIgnoreCase) ||
                text.Contains('£') ||
                text.Contains("GBP", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("CAD", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("AUD", StringComparison.OrdinalIgnoreCase))
            {
                return CurrencyFamily.PeriodDecimal;
            }

            if (text.Contains('€') || text.Contains("EUR", StringComparison.OrdinalIgnoreCase))
            {
                return CurrencyFamily.Euro;
            }

            if (text.Contains('₺') ||
                text.Contains("TL", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("TRY", StringComparison.OrdinalIgnoreCase))
            {
                return CurrencyFamily.TurkishLira;
            }

            // Fallback to requestCulture only if no recognized currency is in text
            if (requestCulture != null)
            {
                string sym = requestCulture.NumberFormat.CurrencySymbol;
                if (sym == "$" || requestCulture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                    return CurrencyFamily.PeriodDecimal;
                if (sym == "€" || requestCulture.Name.Equals("es-ES", StringComparison.OrdinalIgnoreCase))
                    return CurrencyFamily.Euro;
                if (sym == "₺" || requestCulture.Name.Equals("tr-TR", StringComparison.OrdinalIgnoreCase))
                    return CurrencyFamily.TurkishLira;
            }

            return CurrencyFamily.Unknown;
        }

        private static string StripCurrencyIndicators(string text)
        {
            string cleaned = text;
            cleaned = cleaned.Replace("$", "")
                             .Replace("€", "")
                             .Replace("£", "")
                             .Replace("₺", "");

            cleaned = Regex.Replace(cleaned, @"\b(USD|EUR|GBP|TL|TRY|CAD|AUD)\b", "", RegexOptions.IgnoreCase);

            return cleaned.Trim();
        }
    }
}
