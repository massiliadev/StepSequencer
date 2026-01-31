using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Unity.VisualScripting;
using UnityEngine;

public class StepSequencer_Manager : MonoBehaviour
{
    #region Inner Classes

    // Step data class with trigger and pitch
    [System.Serializable]
    public class StepData
    {
        public bool trigger = false;
        public float pitch = 0.5f; // 0.0 to 1.0, maps to button height

        public StepData() { }
        public StepData(bool trigger, float pitch)
        {
            this.trigger = trigger;
            this.pitch = pitch;
        }
    }

    #endregion

    #region Members

    [Header("Audio")]

    public const int Voices = 6;
    public const int Steps = 16;

    // Internals
    private BPMClock BPMMaster;

    private StepData[,] StepPatterns = null;

    private static string SequencePath = "StepSequencer/Sequences";
    private static string SequenceExt = "xml";
    private List<string> SequenceList;

    #endregion

    #region Behaviour

    void Awake()
    {
        CreateDefaultStepData();
        PopulateSequenceList();

        BPMMaster = GameObject.FindAnyObjectByType<BPMClock>();

        if (BPMMaster == null)
        {
            Debug.LogError("StepSequencerUI was unable to find a BPMClock in scene");
        }
    }

    #endregion

    #region XML

    // ---------------- XML Save / Load ----------------
    [System.Serializable]
    public class SequenceData
    {
        [XmlAttribute] public string name = "Sequence";
        
        [XmlArray("Pattern"), XmlArrayItem("Row")]
        public StepData[][] pattern;

        public int voices = Voices;
        public int steps = Steps;

        public SequenceData() { }
        public SequenceData(int voices, int steps)
        {
            this.voices = voices;
            this.steps = steps;
            pattern = new StepData[voices][];
            for (int v = 0; v < voices; v++)
            {
                pattern[v] = new StepData[steps];
                for (int s = 0; s < steps; s++)
                    pattern[v][s] = new StepData(false, 0.5f);
            }
        }
    }

    private void PopulateSequenceList()
    {
        SequenceList = new List<string>();

        DirectoryInfo sequenceDir = new DirectoryInfo($"{Application.dataPath}/{SequencePath}/");
        if (sequenceDir.Exists)
        {
            var files = sequenceDir.EnumerateFiles($"*.{SequenceExt}");
            foreach (var file in files)
                SequenceList.Add(file.Name);
        }
    }

    private string makeFilePath(string filename)
    {
        return Application.dataPath + $"/{SequencePath}/{filename}";
    }

    public void SaveSequence(string filename)
    {
        string filepath = makeFilePath(filename);

        SequenceData seq = new SequenceData(Voices, Steps);
        for (int v = 0; v < Voices; v++)
            for (int s = 0; s < Steps; s++)
                seq.pattern[v][s] = new StepData(StepPatterns[v, s].trigger, StepPatterns[v, s].pitch);

        XmlSerializer serializer = new XmlSerializer(typeof(SequenceData));
        using (FileStream stream = new FileStream(filepath, FileMode.Create))
            serializer.Serialize(stream, seq);
        Debug.Log("Sequence saved to " + filepath);
    }

    public void LoadSequence(string filename)
    {
        string filepath = makeFilePath(filename);

        if (!File.Exists(filepath)) { Debug.LogWarning("File not found: " + filepath); return; }

        try
        {
            XmlSerializer serializer = new XmlSerializer(typeof(SequenceData));
            SequenceData seq = null;
            using (FileStream stream = new FileStream(filepath, FileMode.Open))
            {
                seq = serializer.Deserialize(stream) as SequenceData;
            }

            if (seq != null && seq.pattern != null)
            {
                for (int v = 0; v < Voices && v < seq.pattern.Length; v++)
                {
                    if (seq.pattern[v] == null) continue;
                    for (int s = 0; s < Steps && s < seq.pattern[v].Length; s++)
                    {
                        if (seq.pattern[v][s] != null)
                            StepPatterns[v, s] = new StepData(seq.pattern[v][s].trigger, seq.pattern[v][s].pitch);
                        else
                            StepPatterns[v, s] = new StepData(false, 0.5f);
                    }
                }
                int currentStep = BPMMaster != null ? BPMMaster.CurrentStep() : 0;
                Debug.Log($"Sequence {filename} loaded at step {currentStep}");
            }
            else
            {
                Debug.LogWarning("Sequence file is empty or invalid: " + filepath);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to load sequence from " + filepath + ": " + e.Message);
        }
    }

    #endregion

    #region Public Methods

    // ---------------- Access ----------------
    public bool IsStepActive(int voice, int step)
    {
        return StepPatterns[voice, step]?.trigger ?? false;
    }

    public float GetStepPitch(int voice, int step)
    {
        return StepPatterns[voice, step]?.pitch ?? 0;
    }

    public int GetVoices()
    {
        return Voices;
    }

    public int GetSteps()
    {
        return Steps;
    }

    public int GetCurrentStep()
    {
        return BPMMaster?.CurrentStep() ?? -1;
    }

    public List<string> GetSequenceList()
    {
        return SequenceList;
    }

    // ---------------- Act ----------------
    public void SetStepActive(int voice, int step, bool nextstate)
    {
        if (StepPatterns[voice, step] != null)
        {
            StepPatterns[voice, step].trigger = nextstate;
        }
    }

    public void SetStepPitch(int voice, int step, float nextpitch)
    {
        if (StepPatterns[voice, step] != null)
        {
            StepPatterns[voice, step].pitch = nextpitch;
        }
    }

    public void RequestBPMTransition(float newBPM, string trsShortName, string outShortName)
    {
        var transitionFileName = SequenceList.Find(x => x.ContainsInsensitive(trsShortName));
        var outroFileName = SequenceList.Find(x => x.ContainsInsensitive(outShortName));

        BPMMaster.RequestBPMTransition(newBPM, transitionFileName, outroFileName);
    }

    #endregion

    #region Private Methods

    private void CreateDefaultStepData()
    {
        StepPatterns = new StepData[Voices, Steps];

        // Initialize pattern array with default StepData
        for (int v = 0; v < Voices; v++)
        {
            for (int s = 0; s < Steps; s++)
            {
                if (StepPatterns[v, s] == null)
                    StepPatterns[v, s] = new StepData(false, 0.5f);
            }
        }
    }

    #endregion
}