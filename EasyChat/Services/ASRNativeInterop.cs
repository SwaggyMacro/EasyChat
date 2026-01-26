using System.Runtime.InteropServices;

namespace EasyChat.Services
{
    public class AsrNativeInterop
    {
        private const string DllName = "./Lib/ASRNative"; 

        // 0: Final Result, 1: Partial Result, 2: Error, 3: Canceled
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void RecognitionCallback(int type, [MarshalAs(UnmanagedType.LPUTF8Str)] string result);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool Initialize([MarshalAs(UnmanagedType.LPStr)] string modelPath);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetCallback(RecognitionCallback callback);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void StartRecognition();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void StopRecognition();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void StartLoopbackCapture([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] int[] processIds, int count);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void StopLoopbackCapture();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void PushAudio(byte[] data, int length);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Cleanup();
    }
}