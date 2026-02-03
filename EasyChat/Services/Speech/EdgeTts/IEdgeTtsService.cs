using System.IO;
using System.Threading.Tasks;

namespace EasyChat.Services.Speech.EdgeTts;

public interface IEdgeTtsService
{
    Task SynthesizeAsync(string text, string voice, string outputFile, string rate = "+0%", string volume = "+0%", string pitch = "+0Hz");
    Task<Stream> StreamAsync(string text, string voice, string rate = "+0%", string volume = "+0%", string pitch = "+0Hz");
}
