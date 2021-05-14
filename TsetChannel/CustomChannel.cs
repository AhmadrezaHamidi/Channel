using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TsetChannel.Controllers;

namespace TsetChannel
{
    public class CustomChannel : ICustomChannel
    {
        private Channel<MyDto> channel { get; set; }

        public CustomChannel()
        {
            channel = Channel.CreateUnbounded<MyDto>();
            ReadAsync();
        }

        public async Task WriteAsync(MyDto dto)
        {
            await channel.Writer.WriteAsync(dto);
        }

        public async ValueTask ReadAsync()
        {
            while (await channel.Reader.WaitToReadAsync())
            {
                if (channel.Reader.TryRead(out var msg))
                {
                    // Proccess



                    await Task.Delay(2000);
                    Console.WriteLine((msg as MyDto)?.Time);
                }
            }
        }
    }

    public interface ICustomChannel
    {
        Task WriteAsync(MyDto dto);
    }
}