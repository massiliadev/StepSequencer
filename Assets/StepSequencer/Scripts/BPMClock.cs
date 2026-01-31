using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class BPMClock : MonoBehaviour
{
    #region Members

    [Header("Tempo")]
    public float bpm = 120f;
    public int stepsPerBar = 16;

    [Header("Echo")]
    public int echoBounceCount = 4;

    [Header("References")]
    public StepSequencerUI sequencer;
    public PolyphonicVoiceManager sampler;

    private AudioEchoFilter echoFilter;

    // -------- Transport --------
    private int currentStep = 0;
    private double nextStepTime;
    private double stepDuration;

    // -------- BPM morph state --------
    private bool bpmTransitionActive = false;
    private bool bpmTransitionInitialized = false;

    private float startBPM;
    private float targetBPM;

    private string transitionSequencePath;  // sequence to play during morph bar
    private string mainSequencePath;        // sequence to load on the next bar

    private int stepCounter = 0;
    private bool mainSequenceLoaded = false;

    #endregion

    #region Behaviour

    private void Awake()
    {
        echoFilter = GetComponent<AudioEchoFilter>();
        UpdateFromBPM();
    }

    private void Start()
    {
        nextStepTime = AudioSettings.dspTime;
    }

    private void OnValidate()
    {
        UpdateFromBPM();
    }

    private void Update()
    {
        double dspTime = AudioSettings.dspTime;

        if (dspTime >= nextStepTime)
        {
            TriggerStep(currentStep);

            // ---- Step-based BPM morph ----
            if (bpmTransitionActive && bpmTransitionInitialized)
            {
                OnStep();
            }

            // ---- Step advance ----
            currentStep = (currentStep + 1) % stepsPerBar;
            nextStepTime += stepDuration;

            // ---- Bar start ----
            if (currentStep == 0)
                OnBarStart();
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Request a BPM transition
    /// </summary>
    /// <param name="newBPM">BPM targeted</param>
    /// <param name="transitionSeqPath">sequence to use as transition</param>
    /// <param name="goalSeqPath">sequence to play after transition</param>
    public void RequestBPMTransition(float newBPM, string transitionSeqPath, string goalSeqPath)
    {
        targetBPM = newBPM;
        transitionSequencePath = transitionSeqPath;
        mainSequencePath = goalSeqPath;

        bpmTransitionActive = true;
        bpmTransitionInitialized = false;
        mainSequenceLoaded = false;
        stepCounter = 0;
    }

    /// <summary>
    /// Return current step.
    /// </summary>
    /// <returns></returns>
    public int CurrentStep()
    {
        return currentStep;
    }

    #endregion

    #region Private Methods

    void OnBarStart()
    {
        if (!bpmTransitionActive)
            return;

        if (!bpmTransitionInitialized)
        {
            // ---- Start of morph bar ----
            // Load transition sequence
            if (!string.IsNullOrEmpty(transitionSequencePath))
                sequencer.LoadSequence(transitionSequencePath);

            startBPM = bpm;
            stepCounter = 0;
            mainSequenceLoaded = false;

            bpmTransitionInitialized = true;
        }
        else
        {
            // ---- First step of the next bar after morph ----
            // Load main sequence
            if (!mainSequenceLoaded && !string.IsNullOrEmpty(mainSequencePath))
            {
                sequencer.LoadSequence(mainSequencePath);
                mainSequenceLoaded = true;
            }

            // Morph finished
            bpmTransitionActive = false;
            bpmTransitionInitialized = false;
            stepCounter = 0;
            UpdateFromBPM();
        }
    }

    void OnStep()
    {
        // BPM interpolation step-by-step
        float t = stepCounter / (float)stepsPerBar; // 0 → 1 over the morph bar
        bpm = Mathf.Lerp(startBPM, targetBPM, t);

        UpdateFromBPM();

        stepCounter++;
    }

    // ================= Helpers =================

    void UpdateFromBPM()
    {
        // adjust echofilter (if any) so it produce 3 bounces, adjusted to BPM.
        if (echoFilter != null)
        {
            echoFilter.delay = 60000.0f / bpm;
            echoFilter.decayRatio = Mathf.Pow(0.001f, 1f / echoBounceCount);
        }

        stepDuration = 60.0 / bpm / 4.0; // 16th notes
    }

    void TriggerStep(int step)
    {
        for (int v = 0; v < StepSequencerUI.Voices; v++)
        {
            if (sequencer.IsStepActive(v, step))
            {
                float pitch = sequencer.GetStepPitch(v, step);
                sampler.TriggerSample(v, pitch);
            }
        }
    }

    #endregion
}
