namespace VKBD_nc.Models
{
    public class VkSettings
    {
        public string? AccessToken { get; set; }
        public string? GroupId { get; set; }
        public string? ApiVersion { get; set; } = "5.131";
        public string? ConfirmationCode { get; set; }
    }
}
