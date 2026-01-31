using UnityEngine;
using System.IO;
using System.Xml.Serialization;

public class StepSequencerUI : MonoBehaviour
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

    public StepData[,] StepPatterns = null;

    [Header("UI")]

    // Dimensions
    public int buttonSize = 32;
    public int spacing = 6;
    public int groupSpacing = 16;

    // Drag state for pitch adjustment
    private int draggingVoice = -1;
    private int draggingStep = -1;
    private float dragStartPitch;
    private Vector2 dragStartMousePos;

    public bool isUIVisible = false;

    // Internals
    private BPMClock BPMMaster;
    private static string SequencePath = "StepSequencer/Sequences";

    #endregion

    #region Behaviour

    void Awake()
    {
        CreateStepData();

        BPMMaster = GameObject.FindAnyObjectByType<BPMClock>();
        if (BPMMaster == null)
        {
            Debug.LogError("StepSequencerUI was unable to find a BPMClock in scene");
        }
    }

    void OnGUI()
    {
        if (isUIVisible)
        {
            ProduceUI();
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

    private string makeFilePath(string filename)
    {
        return Application.dataPath + $"/{SequencePath}/{filename}.xml";
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
                Debug.Log("Sequence loaded from " + filepath + " at step " + currentStep);
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

    #region UI Producer

    void ProduceUI()
    {
        Event currentEvent = Event.current;
        
        // Use full screen width with margins
        float screenWidth = Screen.width;
        float screenHeight = Screen.height;
        float margin = 20f;
        float availableWidth = screenWidth - (margin * 2);
        
        Rect areaRect = new Rect(margin, margin, availableWidth, screenHeight - (margin * 2));
        Vector2 mousePos = currentEvent.mousePosition;
        Vector2 localMousePos = mousePos - new Vector2(areaRect.x, areaRect.y);

        GUI.BeginGroup(areaRect);

        // --- Step grid ---
        float labelWidth = 40f;
        float startX = labelWidth + 10f; // X position for first button (after label)
        float startY = 0f;
        float rowSpacing = 10f;
        
        // Calculate button width to fill available space
        // Account for: label, spacing between buttons, group spacing (3 groups of 4 steps = 3 group spacings)
        float totalGroupSpacing = 3 * groupSpacing; // 3 groups (after steps 4, 8, 12)
        float totalButtonSpacing = (Steps - 1) * spacing;
        float availableButtonWidth = availableWidth - startX - 20f; // 20f right margin
        float calculatedButtonWidth = (availableButtonWidth - totalGroupSpacing - totalButtonSpacing) / Steps;
        float actualButtonSize = Mathf.Max(buttonSize, calculatedButtonWidth); // Use at least buttonSize, but expand if needed

        for (int v = 0; v < Voices; v++)
        {
            float rowHeight = buttonSize; // max possible height
            float rowY = startY + v * (rowHeight + rowSpacing);

            // Voice label
            GUI.Label(new Rect(0, rowY, labelWidth, rowHeight), $"V{v + 1}");

            float currentX = startX;

            for (int s = 0; s < Steps; s++)
            {
                if (s % 4 == 0 && s != 0) currentX += groupSpacing;

                if (s == BPMMaster.CurrentStep())
                    GUI.color = StepPatterns[v, s].trigger ? Color.yellow : Color.grey;
                else
                    GUI.color = StepPatterns[v, s].trigger ? Color.red : Color.black;

                // Button height based on pitch (0.0 to 1.0 maps to buttonSize/4 to buttonSize)
                float h = Mathf.Lerp(buttonSize / 4f, buttonSize, StepPatterns[v, s].pitch);
                float buttonY = rowY + (rowHeight - h) / 2f; // Center vertically

                Rect buttonRect = new Rect(currentX, buttonY, actualButtonSize, h);

                // Handle mouse events for dragging
                if (buttonRect.Contains(localMousePos))
                {
                    if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
                    {
                        // Start drag
                        draggingVoice = v;
                        draggingStep = s;
                        dragStartPitch = StepPatterns[v, s].pitch;
                        dragStartMousePos = localMousePos;
                        currentEvent.Use();
                    }
                    else if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0)
                    {
                        // End drag or click
                        if (draggingVoice == v && draggingStep == s)
                        {
                            // If we didn't drag much, toggle trigger
                            if (Vector2.Distance(localMousePos, dragStartMousePos) < 5f)
                            {
                                StepPatterns[v, s].trigger = !StepPatterns[v, s].trigger;
                            }
                        }
                        draggingVoice = -1;
                        draggingStep = -1;
                        currentEvent.Use();
                    }
                }

                // Handle dragging
                if (draggingVoice == v && draggingStep == s && currentEvent.type == EventType.MouseDrag)
                {
                    float dragDelta = dragStartMousePos.y - localMousePos.y; // Inverted: drag up = increase pitch
                    float sensitivity = 0.01f; // Adjust this to change drag sensitivity
                    float newPitch = dragStartPitch + (dragDelta * sensitivity);
                    StepPatterns[v, s].pitch = Mathf.Clamp01(newPitch);
                    currentEvent.Use();
                }

                // Draw button
                if (GUI.Button(buttonRect, ""))
                {
                    // This handles clicks when not dragging
                    if (draggingVoice != v || draggingStep != s)
                    {
                        StepPatterns[v, s].trigger = !StepPatterns[v, s].trigger;
                    }
                }

                // Draw pitch value (0-12 semitones) inside button
                int semitones = Mathf.RoundToInt(StepPatterns[v, s].pitch * 12f);
                string pitchText = semitones.ToString();
                
                // Use a style for the text
                GUIStyle textStyle = new GUIStyle(GUI.skin.label);
                textStyle.alignment = TextAnchor.MiddleCenter;
                textStyle.normal.textColor = StepPatterns[v, s].trigger ? Color.white : Color.grey;
                textStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(actualButtonSize * 0.4f)); // Scale font with button size
                
                GUI.Label(buttonRect, pitchText, textStyle);

                currentX += actualButtonSize + spacing;
            }

            GUI.color = Color.white;
        }

        GUI.EndGroup();

        // Sequence control panel below the step grid
        float controlPanelY = margin + (Voices * (buttonSize + 10f)) + 20f;
        float controlPanelWidth = screenWidth - (margin * 2);
        GUILayout.BeginArea(new Rect(margin, controlPanelY, controlPanelWidth, 200));

        // --- Sequence Control Panel ---
        string[] seqNames = { "seq1", "seq2", "seq3", "seq4" };

        GUILayout.BeginHorizontal();

        for (int i = 0; i < seqNames.Length; i++)
        {
            GUILayout.BeginVertical();
            GUILayout.Label(seqNames[i], GUILayout.Width(80));

            // Load button
            if (GUILayout.Button("Load", GUILayout.Width(80), GUILayout.Height(buttonSize)))
            {
                LoadSequence(seqNames[i]);
            }

            GUILayout.Space(spacing);

            // Save button
            if (GUILayout.Button("Save", GUILayout.Width(80), GUILayout.Height(buttonSize)))
            {

                SaveSequence(seqNames[i]);
            }

            GUILayout.EndVertical();

            GUILayout.Space(groupSpacing);
        }

        GUILayout.EndHorizontal();
        GUILayout.Space(groupSpacing);
        
        // Tempo buttons in horizontal layout to fit all
        GUILayout.BeginHorizontal();

        AddTempoRequestButton(80,   1);
        AddTempoRequestButton(100,  4);
        AddTempoRequestButton(120,  4);
        AddTempoRequestButton(140,  1);
        AddTempoRequestButton(160,  3);

        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    #endregion

    #region Public Methods

    // ---------------- Access ----------------
    public bool IsStepActive(int voice, int step)
    {
        return StepPatterns[voice, step].trigger;
    }

    public float GetStepPitch(int voice, int step)
    {
        return StepPatterns[voice, step].pitch;
    }

    public void AddTempoRequestButton(int bpm, int outro = 1, int transit = 2)
    {
        if (GUILayout.Button($"Request Change {bpm}"))
        {
            BPMMaster.RequestBPMTransition(
                    bpm,
                    $"Seq{transit}",
                    $"Seq{outro}");

            GUILayout.Space(spacing);
        }
    }

    #endregion

    #region Private Methods

    private void CreateStepData()
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