using System;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Integrations.Sample.Editor
{
    [FunctionCallRenderer(typeof(SampleTools), nameof(SampleTools.GetWeather))]
    class GetWeatherRenderer : DefaultFunctionCallRenderer
    {
        public override void OnCallSuccess(string functionId, Guid callId, IFunctionCaller.CallResult result)
        {
            var weather = result.GetTypedResult<SampleTools.WeatherOutput>();
            var typeText = weather.Type switch {
                SampleTools.WeatherOutput.WeatherType.Sun => "☀️",
                SampleTools.WeatherOutput.WeatherType.Cloud => "☁️",
                SampleTools.WeatherOutput.WeatherType.Rain => "🌧️",
                SampleTools.WeatherOutput.WeatherType.Snow => "❄️",
                _ => throw new ArgumentOutOfRangeException()
            };
            Add(FunctionCallUtils.CreateContentLabel($"{typeText} {weather.Temperature}°C"));
        }
    }
}
