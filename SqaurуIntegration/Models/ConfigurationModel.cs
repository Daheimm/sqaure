
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;


namespace SquareIntegration.Services
{
    public class ConfigurationModel : Model
    {

        [Required]
        public string ApplicationId { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public string ApplicationSecret { get; set; }

        public string AccessToken { get; set; }

        public bool UseSandbox { get; set; }

        public string MessageToken { get; set; } = "Token not received!";

        public string LocationId { get; set; }
        public string Locations { get; set; }

        [Required]
        public string CaCafeId { get; set; }

        public IList<ListSquareConnect> ListSquareConnects { get; set; }

    }
}

