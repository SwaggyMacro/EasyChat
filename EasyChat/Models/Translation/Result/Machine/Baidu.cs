using System.Collections.Generic;

namespace EasyChat.Models.Translation.Result.Machine;

public class Baidu
{
    public string From { get; set; } = null!;
    public string To { get; set; } = null!;
    public List<CTransResult> TransResult { get; set; } = null!;
    public string ErrorCode { get; set; } = null!;
    public string ErrorMsg { get; set; } = null!;

    public class CTransResult
    {
        public string Src { get; set; } = null!;
        public string Dst { get; set; } = null!;
    }
}