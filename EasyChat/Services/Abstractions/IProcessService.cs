using System.Collections.ObjectModel;
using EasyChat.Models;

namespace EasyChat.Services.Abstractions;

public interface IProcessService
{
    ObservableCollection<ProcessInfo> Processes { get; }
    void RefreshProcesses();
}
