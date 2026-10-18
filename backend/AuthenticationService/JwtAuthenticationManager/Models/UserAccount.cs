namespace JwtAuthenticationManager.Models
{
    public class UserAccount
    {
        public required string UserName { get; set; }
        public required string Password { get; set; }
        public required string Role { get; set; }
    }
}
