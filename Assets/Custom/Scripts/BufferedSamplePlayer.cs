using UnityEngine;

public class BufferedSamplePlayer : MonoBehaviour
{
    public AudioClip sample;
    public KeyCode triggerKey = KeyCode.Space;

    private float[] sampleData;
    private int samplePosition = 0;
    private bool isPlaying = false;

    public int fadeOutSamples = 128;

    void Start()
    {
        sampleData = new float[sample.samples];
        sample.GetData(sampleData, 0);
    }

    void Update()
    {
        if (Input.GetKeyDown(triggerKey))
        {
            Debug.Log("trigerred");
            samplePosition = 0;
            isPlaying = true;
        }
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!isPlaying) return;

        for (int i = 0; i < data.Length; i += channels)
        {
            if (samplePosition >= sampleData.Length)
            {
                isPlaying = false;
                break;
            }

            float sampleValue = sampleData[samplePosition];

            // Fade-out to avoid click
            if (samplePosition > sampleData.Length - fadeOutSamples)
            {
                int fadeIndex = sampleData.Length - samplePosition;
                sampleValue *= fadeIndex / (float)fadeOutSamples;
            }

            samplePosition++;

            // copy the datas.
            for (int c = 0; c < channels; c++)
            {
                data[i + c] += sampleValue;
            }
        }
    }
}
