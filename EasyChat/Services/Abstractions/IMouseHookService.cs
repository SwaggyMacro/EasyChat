using System;

namespace EasyChat.Services.Abstractions;

public interface IMouseHookService
{
    event EventHandler<SimpleMouseEventArgs> MouseUp;
    event EventHandler<SimpleMouseEventArgs> MouseDown;
    event EventHandler<SimpleMouseEventArgs> MouseDoubleClick;
    void Start();
    void Stop();
}

public class SimpleMouseEventArgs : EventArgs
{
    public int X { get; }
    public int Y { get; }
    
    public SimpleMouseEventArgs(int x, int y)
    {
        X = x;
        Y = y;
    }
}
