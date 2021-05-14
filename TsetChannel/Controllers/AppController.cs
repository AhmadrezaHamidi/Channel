using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace TsetChannel.Controllers
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
            await _channel.WriteAsync(new MyDto(DateTime.Now.ToString()));

            var rng = new Random();

            return new List<WeatherForecast>();
        }
    }

    public class MyDto
    {
        public MyDto(string time)
        {
            Time = time;
        }
        public string Time { get; set; }
    }
}
