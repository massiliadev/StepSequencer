using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
public class PolyphonicVoiceManager : MonoBehaviour
{
    #region Inner Classes

    [System.Serializable]
    public class Sample
    {
        public string name;
        public AudioClip clip;
        public bool allowPolyphony = true;
        public float gain;

        [HideInInspector] public float[] data;
    }

    class Voice
    {
        public float[] data;
        public float position;        // Fractional position in source frames (for pitch shifting)
        public bool active;
        public float gain;
        public float pitch;           // Pitch value (0-1), converted to frequency multiplier
        public float pitchMultiplier; // Frequency multiplier: 2^pitch
        public float sourceFrequency; // Clip sample rate (Hz) for correct resampling
    }

    #endregion

    #region Members

    [Header("Samples")]
    public Sample[] samples;

    [Header("Polyphony & Timing")]
    public int maxVoices = 16;
    public int fadeOutSamples = 128;

    [Header("Mix Settings")]
    [Range(0f, 1f)]
    public float masterGain = 0.3f;
    public bool normalizeByActiveVoices = true; // prevents overload when many voices play

    private List<Voice> voices = new List<Voice>();
    private float outputSampleRate = 48000f; // Cached; set in Start from AudioSettings

    #endregion

    #region Behaviours

    void Start()
    {
        outputSampleRate = AudioSettings.outputSampleRate;

        // Load all sample data into memory (mono frames for correct pitch and stereo compatibility)
        foreach (var s in samples)
        {
            if (s.clip != null)
                LoadSampleAsMonoFrames(s);
        }
    }

    /// <summary>Load clip into s.data as mono frames (one sample per time step) for correct resampling and stereo handling.</summary>
    private void LoadSampleAsMonoFrames(Sample s)
    {
        var clip = s.clip;
        int totalSamples = clip.samples;
        int channels = clip.channels;
        int frameCount = totalSamples / channels;

        s.data = new float[frameCount];
        if (channels == 1)
        {
            clip.GetData(s.data, 0);
            return;
        }
        float[] interleaved = new float[totalSamples];
        clip.GetData(interleaved, 0);
        for (int f = 0; f < frameCount; f++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++)
                sum += interleaved[f * channels + c];
            s.data[f] = sum / channels;
        }
    }

    #endregion

    #region public Methods

    /// <summary>
    /// Trigger a sample by index
    /// </summary>
    public void TriggerSample(int index)
    {
        TriggerSample(index, 0.5f); // Default pitch (middle)
    }

    /// <summary>
    /// Trigger a sample by index with pitch (0-1, where 1 = one octave up)
    /// </summary>
    public void TriggerSample(int index, float pitch)
    {
        if (index < 0 || index >= samples.Length) return;

        var sample = samples[index];

        Voice voice;

        float freq = sample.clip != null ? sample.clip.frequency : outputSampleRate;
        float mult = Mathf.Pow(2f, pitch); // 2^pitch: 0->1.0, 1->2.0 (one octave)

        if (!sample.allowPolyphony)
        {
            // Check if a voice with this sample is already active
            voice = voices.Find(v => v.active && v.data == sample.data);
            if (voice != null)
            {
                // Restart previous instance
                voice.position = 0f;
                voice.pitch = pitch;
                voice.pitchMultiplier = mult;
                voice.sourceFrequency = freq;
                return; // do not create a new voice
            }
        }

        // Polyphonic or no active instance → get a free voice
        voice = GetFreeVoice();
        voice.data = sample.data;
        voice.gain = sample.gain;
        voice.position = 0f;
        voice.pitch = pitch;
        voice.pitchMultiplier = mult;
        voice.sourceFrequency = freq;
        voice.active = true;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Get an available voice or steal the oldest
    /// </summary>
    private Voice GetFreeVoice()
    {
        foreach (var v in voices)
        {
            if (!v.active) return v;
        }

        if (voices.Count < maxVoices)
        {
            var v = new Voice();
            voices.Add(v);
            return v;
        }

        // Voice stealing: reuse oldest
        return voices[0];
    }

    /// <summary>
    /// Hyperbolic tangent soft limiter
    /// </summary>
    private float Tanh(float x)
    {
        float e2x = Mathf.Exp(2f * x);
        return (e2x - 1f) / (e2x + 1f);
    }

    #endregion

    #region AudioFilter

    /// <summary>
    /// Audio DSP callback
    /// </summary>
    void OnAudioFilterRead(float[] data, int channels)
    {
        // Clear output buffer
        for (int i = 0; i < data.Length; i++)
            data[i] = 0f;

        // Count active voices (for optional normalization)
        int activeVoiceCount = 0;
        if (normalizeByActiveVoices)
        {
            foreach (var v in voices)
                if (v.active) activeVoiceCount++;
            activeVoiceCount = Mathf.Max(1, activeVoiceCount);
        }

        // Mix all voices
        foreach (var voice in voices)
        {
            if (!voice.active) continue;

            for (int i = 0; i < data.Length; i += channels)
            {
                if (voice.position >= voice.data.Length)
                {
                    voice.active = false;
                    break;
                }

                // Pitch-shifted sample reading with linear interpolation
                float sampleValue = 0f;
                
                // Get integer and fractional parts of position
                int posInt = Mathf.FloorToInt(voice.position);
                float posFrac = voice.position - posInt;
                
                if (posInt < voice.data.Length - 1)
                {
                    // Linear interpolation between two sample points
                    sampleValue = Mathf.Lerp(voice.data[posInt], voice.data[posInt + 1], posFrac);
                }
                else
                {
                    // At or beyond end of sample
                    sampleValue = voice.data[Mathf.Min(posInt, voice.data.Length - 1)];
                }

                // Fade-out to avoid clicks
                if (voice.position > voice.data.Length - fadeOutSamples)
                {
                    float fadeIndex = voice.data.Length - voice.position;
                    sampleValue *= fadeIndex / (float)fadeOutSamples;
                }

                // Advance position by pitch multiplier, scaled by source vs output sample rate for correct pitch
                float advance = voice.pitchMultiplier * (voice.sourceFrequency / outputSampleRate);
                voice.position += advance;

                // Apply gain (master + optional normalization)
                float gain = masterGain * voice.gain;
                if (normalizeByActiveVoices)
                    gain /= activeVoiceCount;

                // Mix into all channels
                for (int c = 0; c < channels; c++)
                {
                    data[i + c] += sampleValue * gain;
                }
            }
        }

        // Apply soft-limiter to final mix
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = Tanh(data[i]);
        }
    }

    #endregion
}
