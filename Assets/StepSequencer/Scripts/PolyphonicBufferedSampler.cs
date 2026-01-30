using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
public class PolyphonicBufferedSampler : MonoBehaviour
{
    [System.Serializable]
    public class Sample
    {
        public string name;
        public AudioClip clip;
        public bool allowPolyphony = true;
        public float gain;

        [HideInInspector] public float[] data;
    }

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

    class Voice
    {
        public float[] data;
        public float position;        // Changed to float for fractional position (pitch shifting)
        public bool active;
        public float gain;
        public float pitch;           // Pitch value (0-1), converted to frequency multiplier
        public float pitchMultiplier;  // Frequency multiplier: 2^pitch
    }

    void Start()
    {
        // Load all sample data into memory
        foreach (var s in samples)
        {
            if (s.clip != null)
            {
                s.data = new float[s.clip.samples];
                s.clip.GetData(s.data, 0);
            }
        }
    }

    void Update()
    {
        // Optional test trigger
        if (Input.GetKeyDown(KeyCode.Space))
        {
            TriggerSample(Random.Range(0, samples.Length));
        }
    }

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

        if (!sample.allowPolyphony)
        {
            // Check if a voice with this sample is already active
            voice = voices.Find(v => v.active && v.data == sample.data);
            if (voice != null)
            {
                // Restart previous instance
                voice.position = 0f;
                voice.pitch = pitch;
                voice.pitchMultiplier = Mathf.Pow(2f, pitch); // 2^pitch: 0->1.0, 1->2.0
                return; // do not create a new voice
            }
        }

        // Polyphonic or no active instance → get a free voice
        voice = GetFreeVoice();
        voice.data = sample.data;
        voice.gain = sample.gain;
        voice.position = 0f;
        voice.pitch = pitch;
        voice.pitchMultiplier = Mathf.Pow(2f, pitch); // 2^pitch: 0->1.0, 1->2.0
        voice.active = true;
    }


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

                // Advance position by pitch multiplier (resampling)
                voice.position += voice.pitchMultiplier;

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

    /// <summary>
    /// Hyperbolic tangent soft limiter
    /// </summary>
    private float Tanh(float x)
    {
        float e2x = Mathf.Exp(2f * x);
        return (e2x - 1f) / (e2x + 1f);
    }
}
