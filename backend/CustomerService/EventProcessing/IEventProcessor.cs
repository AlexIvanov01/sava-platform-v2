using System.Threading.Tasks;

namespace CustomerService.EventProcessing;

public interface IEventProcessor
{
    Task ProcessEvent(string message);
}
