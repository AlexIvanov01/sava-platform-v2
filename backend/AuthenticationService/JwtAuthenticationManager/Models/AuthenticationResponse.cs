namespace JwtAuthenticationManager.Models
{
    public class AuthenticationResponse
    {
        public required string UserName { get; set; }
        public required string JwtToken { get; set; }
        public int ExpiresIn { get; set; }
    }
}
