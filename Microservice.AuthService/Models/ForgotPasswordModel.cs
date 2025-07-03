using System.ComponentModel.DataAnnotations;

namespace Microservice.AuthService.Models
{
    public class ForgotPasswordModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; }
    }
}
