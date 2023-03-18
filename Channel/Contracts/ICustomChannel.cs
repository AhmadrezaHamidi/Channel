using Channel.Dtos;

namespace Channel.Contracts
{
    public interface ICustomChannel
    {
        Task WriteAsync(TimeDto dto);
    }
}