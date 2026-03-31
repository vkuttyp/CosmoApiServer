using System.Threading.Tasks;

namespace Coravel.Events.Interfaces
{
    public interface IDispatcher
    {
        Task Broadcast(IEvent toBroadcast);
    }
}
