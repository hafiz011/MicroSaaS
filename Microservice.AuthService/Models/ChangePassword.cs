using System.ComponentModel.DataAnnotations;

namespace Microservice.AuthService.Models
{
    public class ChangePasswordModel
    {
        public string currentPassword { get; set; }
        public string newPassword { get; set; }
    }
}
