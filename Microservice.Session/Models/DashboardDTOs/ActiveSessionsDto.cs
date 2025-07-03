namespace Microservice.Session.Models.DashboardDTOs
{
    public class ActiveSessionsDto
    {
        public string User_Id { get; set; }
        public string Ip_Address { get; set; }
        public string Location { get; set; }
        public string Device { get; set; }
        public string Login_Time { get; set; }
    }
}
