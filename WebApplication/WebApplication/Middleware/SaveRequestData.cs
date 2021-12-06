using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using StackExchange.Redis;

using System;
using System.Linq;
using System.Threading.Tasks;

namespace WebApplication.Middleware
{
    public class SaveRequestData
    {
        private IConnectionMultiplexer _multiplexer;
        private Random _random;

        private const string _redisCacheKey = "keys_cache";

        public SaveRequestData(RequestDelegate next, IConnectionMultiplexer multiplexer)
        {
            _multiplexer = multiplexer;
            _random = new Random();
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var dbRedis = _multiplexer.GetDatabase(0);
            try
            {
                await dbRedis.StringSetAsync(context.TraceIdentifier, "");
            }
            catch
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return;
            }
            var collection = _multiplexer.GetServer(_multiplexer.GetEndPoints().First()).Keys();

            var ttl = await dbRedis.KeyExistsAsync(_redisCacheKey) ? await dbRedis.KeyTimeToLiveAsync(_redisCacheKey) : null;
            long? delta = null;

            if (ttl.HasValue)
            {
                var cache = await dbRedis.StringGetAsync(_redisCacheKey);

                if (cache.HasValue)
                {
                    var response = JsonConvert.DeserializeObject<CacheObj>(cache);
                    delta = response.Delta;

                    if (DateTimeOffset.Now.ToUnixTimeSeconds() - response.Delta * 1 * Math.Log(_random.Next(0, 1)) < ttl.Value.Ticks)
                    {
                        await context.Response.WriteAsync($"<html><body>Data saved. Data get from cache:<br />{response.Data}</body></html>");
                        return;
                    }
                }
            }

            var data = string.Join("<br />", collection);
            await dbRedis.StringSetAsync(_redisCacheKey, JsonConvert.SerializeObject(new CacheObj
            {
                Delta = (delta ?? DateTimeOffset.Now.ToUnixTimeSeconds()) - DateTimeOffset.Now.ToUnixTimeSeconds(),
                Data = data
            }), TimeSpan.FromSeconds(20));
            await context.Response.WriteAsync($"<html><body>Data saved. Data:<br />{data}</body></html>");
            return;
        }
    }
}
