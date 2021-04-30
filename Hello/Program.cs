using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AspectCore.Extensions.Reflection;
using Microsoft.Extensions.DependencyInjection;
using WebApiClientCore;
using WebApiClientCore.Attributes;
using WebApiClientCore.Internals;

namespace Hello
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                var services = new ServiceCollection();
                services.AddHttpApi<IMarketApi>().ConfigureHttpApi(x => {
                    x.KeyValueSerializeOptions.IgnoreNullValues = true;
                    x.JsonDeserializeOptions.Converters.Add(new Array2ClassConverter<Kline>());
                    x.JsonDeserializeOptions.Converters.Add(new DatetimeOffsetConverter());
                });
                var provider = services.BuildServiceProvider();
                var marketApi = provider.GetService<IMarketApi>();
                var data = await marketApi.GetKlinesDynamic("ETHBTC", "1m");
                var json = JsonSerializer.Serialize(data);
                var result = JsonSerializer.Deserialize<IEnumerable<Kline>>(json, new JsonSerializerOptions()
                {
                    Converters = { new Array2ClassConverter<Kline>(), new DatetimeOffsetConverter() }
                });
                //Error
                result = await marketApi.GetKlines("ETHBTC", "1m");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }


    }

    [HttpHost("https://api.binance.com")]
    public interface IMarketApi
    {
        [HttpGet("/api/v3/klines")]
        Task<dynamic> GetKlinesDynamic(string symbol, string interval, DateTimeOffset? start = null, DateTimeOffset? end = null, [Range(1, 1000)] int? limit = null);
        [HttpGet("/api/v3/klines")]
        Task<IEnumerable<Kline>> GetKlines(string symbol, string interval, DateTimeOffset? start = null, DateTimeOffset? end = null, [Range(1, 1000)] int? limit = null);
    }

    [AttributeUsage(AttributeTargets.Property)]

    public class ArrayIndexAttribute : Attribute
    {
        public ArrayIndexAttribute()
        {

        }

        public int Index { get; set; }
    }

    public class Array2ClassConverter<T> : JsonConverter<T> where T : new()
    {
        private static Dictionary<int, (Type Type, PropertyReflector Reflector)[]> _dict = new Dictionary<int, (Type, PropertyReflector)[]>();
        static Array2ClassConverter()
        {
            foreach (var property in typeof(T).GetProperties())
            {
                var reflector = property.GetReflector();
                if (reflector.GetCustomAttribute<ArrayIndexAttribute>() is { Index: >= 0 } attribute)
                {
                    if (_dict.ContainsKey(attribute.Index))
                    {
                        _dict[attribute.Index] = _dict[attribute.Index].Append((property.PropertyType, reflector)).ToArray();
                    }
                    else
                    {
                        _dict[attribute.Index] = new (Type Type, PropertyReflector Reflector)[] { (property.PropertyType, reflector) };
                    }
                }
            }
        }
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException();
            }
            reader.Read();
            var result = new T();
            var i = 0;
            while (reader.TokenType != JsonTokenType.EndArray)
            {
                if (_dict.ContainsKey(i))
                {
                    foreach(var x in _dict[i])
                    {
                        x.Reflector.SetValue(result, JsonSerializer.Deserialize(ref reader, x.Type, options));
                    }
                }
                reader.Read();
                i++;
            }
            return result;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }

    public class DatetimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var timestamp = reader.GetInt64();
            return DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value.ToUnixTimeMilliseconds());
        }
    }

    public class Kline
    {
        [ArrayIndex(Index = 0)]
        public DateTimeOffset Start { get; set; }
    }
}
