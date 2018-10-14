using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Runtime.InteropServices;

namespace pic2wav
{
    class Program
    {
        static void WriteWavHeader(BinaryWriter writer, uint numSamples, uint sampleRate, ushort numChannels, ushort bytesPerSample)
        {
            // http://soundfile.sapp.org/doc/WaveFormat/
            // RIFF header
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + numSamples * numChannels * bytesPerSample);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            // fmt subchunk
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);           // PCM
            writer.Write((ushort)1);    // Linear quantization
            writer.Write(numChannels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * bytesPerSample * numChannels);
            writer.Write((ushort)(bytesPerSample * numChannels));
            writer.Write((ushort)(8 * bytesPerSample));
            // Data subchunk
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(numSamples * bytesPerSample * numChannels);
        }

        static void WriteImageToWav(string imageFile, string waveFile)
        {
            const uint sampleRate = 48000;
            const ushort numChannels = 1;
            const ushort bytesPerSample = 2; // in bytes - 8bit PCM

            const int requiredResolution = 256;
            var image = (System.Drawing.Bitmap)System.Drawing.Image.FromFile(imageFile);
            if (image.Width != requiredResolution && image.Height != requiredResolution)
            {
                Console.WriteLine($"Only {requiredResolution}x{requiredResolution} greyscale pictures are supported");
                return;
            }

            // The shadertoy runs at 60fps. Two frames take the frequencies to average to a single line.
            // That means that we have 2 * 1/60s per line
            const float secondsPerLine = 2.0f * 1.0f / 60.0f;
            const uint samplesPerLine = (uint)(secondsPerLine * sampleRate + 0.5);
            const uint numSamples = (uint)(samplesPerLine * requiredResolution);

            // Shannon theorem says our max frequency is sampleRate/2, but reality looks different in our pipeline.
            // These values have been acquired empirically by checking what our shadertoy actually displays for a test image/sound.
            const float maxFrequency = sampleRate / 2.0f * 0.7f;
            const float xToFrequency = maxFrequency / requiredResolution;

            
            // Read pixels
            float[] maxAmplitudes = new float[requiredResolution];
            float[,] pixels = new float[requiredResolution, requiredResolution]; // y,x
            for (int imageY = 0; imageY<requiredResolution; ++imageY)
            {
                for (int imageX = 0; imageX<requiredResolution; ++imageX)        
                {
                    float value =  image.GetPixel(imageX, imageY).R / 255.0f;
                    pixels[imageY, imageX] = value;
                    maxAmplitudes[imageY] += value;
                }
            }

            // Compute and write wav data.
            using (var f = new FileStream(waveFile, FileMode.Create))
            {
                using (var wr = new BinaryWriter(f))
                {
                    WriteWavHeader(wr, numSamples, sampleRate, numChannels, bytesPerSample);

                    uint sampleIndex = 0;
                    for (int imageY = 0; imageY<requiredResolution; ++imageY)
                    {
                        for (uint sampleIndexLine = 0; sampleIndexLine < samplesPerLine; ++sampleIndexLine, ++sampleIndex)
                        {
                            float t = (float)sampleIndex / sampleRate;
                            float rawSignal = 0.0f;
                            for (int imageX = 0; imageX<requiredResolution; ++imageX)        
                            {
                                float frequency = imageX * xToFrequency;
                                float amplitude = pixels[imageY, imageX];
                                rawSignal += (float)Math.Sin(t * Math.PI * 2.0f * frequency) * amplitude;
                            }
                            rawSignal /= maxAmplitudes[imageY];

                            // Fade line in/out.
                            // This improves quality tremendously as we get fewer ghost frequencies when switching from line to line.
                            // (experimented with linear and square fades before, gauss is unsurprisngly better)
                            float factor = (float)sampleIndexLine / (samplesPerLine - 1) * 2.0f - 1.0f;
                            const float gaussVar = 4.0f;
                            rawSignal *= (float)Math.Exp(-factor * factor * gaussVar);

                            short quantizedSignal = (short)Math.Clamp(rawSignal * short.MaxValue - 0.5, short.MinValue, short.MaxValue);
                            wr.Write(quantizedSignal);
                        }
                    }
                }
            }
        }

        static void Main(string[] args)
        {
            WriteImageToWav("line.png", "line.wav");
         //   WriteImageToWav("purple.png", "purple.wav");
        }
    }
}
