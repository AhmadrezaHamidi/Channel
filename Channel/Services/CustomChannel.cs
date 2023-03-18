using Channel.Contracts;
using Channel.Dtos;
using System.Threading.Channels;

namespace Channel.Services
{
    public class CustomChannel : ICustomChannel
    {
        private Channel<TimeDto> _channel { get; set; }

        public CustomChannel()
        {
            _channel = System.Threading.Channels.Channel.CreateUnbounded<TimeDto>();
            ReadAsync();
        }

        public async Task WriteAsync(TimeDto dto)
        {
            await _channel.Writer.WriteAsync(dto);
        }

        public async ValueTask ReadAsync()
        {
            while (await _channel.Reader.WaitToReadAsync())
            {
                if (_channel.Reader.TryRead(out var msg))
                {
                    // Proccess

                    await Task.Delay(2000);
                    Console.WriteLine((msg as TimeDto)?.Time);
                }
            }
        }
    }
}