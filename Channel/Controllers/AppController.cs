using Channel.Contracts;
using Channel.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Channel.Controllers
{
    [ApiController]
    [Route("[controller]/[action]")]
    public class AppController : ControllerBase
    {
        private readonly ILogger<AppController> _logger;
        private readonly ICustomChannel _channel;

        public AppController(ILogger<AppController> logger, ICustomChannel channel)
        {
            _logger = logger;
            _channel = channel;
        }

        [HttpGet]
        public async Task<IEnumerable<WeatherForecast>> Get()
        {
            await _channel.WriteAsync(new TimeDto(DateTime.Now.ToString()));
            return new List<WeatherForecast>();
        }
    }
}