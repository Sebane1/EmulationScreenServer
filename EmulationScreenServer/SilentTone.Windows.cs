using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

public class SilentTone
{
    private WaveOutEvent? _output;
    private ISampleProvider? _provider;

    public void Start()
    {
        var sine = new SignalGenerator
        {
            Gain = 0.0001,
            Frequency = 440,
            Type = SignalGeneratorType.Sin
        };

        _provider = sine;
        _output = new WaveOutEvent();
        _output.Init(_provider);
        _output.Play();
    }

    public void Stop()
    {
        _output?.Stop();
        _output?.Dispose();
        _output = null;
        _provider = null;
    }
}
