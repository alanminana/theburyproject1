using System.Collections.Generic;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models
{
    public class PrequalificationResult
    {
        public PrequalificationStatus Status { get; set; } = PrequalificationStatus.Indeterminate;
        public string Recommendation { get; set; } = string.Empty;
        public bool? MeetsThirtyPercentPolicy { get; set; }
        public List<string> Flags { get; set; } = new();
    }
}
