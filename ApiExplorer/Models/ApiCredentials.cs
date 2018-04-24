using System.ComponentModel.DataAnnotations;

namespace SSATB.ApiExplorer.Models
{
    public class ApiCredentials
    {
        [Display(Name="Client ID"), Required]
        public string ClientId { get; set; }

        [Display(Name = "Client Secret"), Required]
        public string ClientSecret { get; set; }

        [Display(Name = "School Code"), Required]
        public string OrgId { get; set; }
    }
}