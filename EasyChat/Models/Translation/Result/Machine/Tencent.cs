namespace EasyChat.Models.Translation.Result.Machine;

public class Tencent
{
    public CResponse Response { get; set; } = null!;

    public class CResponse
    {
        public string RequestId { get; set; } = null!;
        public string Source { get; set; } = null!;
        public string Target { get; set; } = null!;
        public string TargetText { get; set; } = null!;
        public int UsedAmount { get; set; }
    }
}