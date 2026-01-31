using UnityEngine;
using System.IO;
using System.Xml.Serialization;

public class StepSequencer_UI : MonoBehaviour
{
    #region Members

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

    private StepSequencer_Manager stepSequencerManager;
    private int selectedSequenceIndex = 0;

    #endregion

    #region Behaviour

    void Awake()
    {
        stepSequencerManager = GameObject.FindAnyObjectByType<StepSequencer_Manager>();
        if (stepSequencerManager == null)
        {
            Debug.LogError("StepSequencer_UI was unable to find a StepSequencer_Manager in scene");
        }
    }

    void OnGUI()
    {
        if (stepSequencerManager != null)
        {
            ProduceUI();
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
        float totalButtonSpacing = (stepSequencerManager.GetSteps() - 1) * spacing;
        float availableButtonWidth = availableWidth - startX - 20f; // 20f right margin
        float calculatedButtonWidth = (availableButtonWidth - totalGroupSpacing - totalButtonSpacing) / stepSequencerManager.GetSteps();
        float actualButtonSize = Mathf.Max(buttonSize, calculatedButtonWidth); // Use at least buttonSize, but expand if needed

        for (int v = 0; v < stepSequencerManager.GetVoices(); v++)
        {
            float rowHeight = buttonSize; // max possible height
            float rowY = startY + v * (rowHeight + rowSpacing);

            // Voice label
            GUI.Label(new Rect(0, rowY, labelWidth, rowHeight), $"V{v + 1}");

            float currentX = startX;

            for (int s = 0; s < stepSequencerManager.GetSteps(); s++)
            {
                if (s % 4 == 0 && s != 0) currentX += groupSpacing;

                bool stepActive = stepSequencerManager.IsStepActive(v, s);
                float stepPitch = stepSequencerManager.GetStepPitch(v, s);

                if (s == stepSequencerManager.GetCurrentStep())
                    GUI.color = stepActive ? Color.yellow : Color.grey;
                else
                    GUI.color = stepActive ? Color.red : Color.black;

                // Button height based on pitch (0.0 to 1.0 maps to buttonSize/4 to buttonSize)
                float h = Mathf.Lerp(buttonSize / 4f, buttonSize, stepPitch);
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
                        dragStartPitch = stepPitch;
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
                                stepSequencerManager.SetStepActive(v, s, !stepActive);
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
                    stepSequencerManager.SetStepPitch(v, s, Mathf.Clamp01(newPitch));
                    currentEvent.Use();
                }

                // Draw button
                if (GUI.Button(buttonRect, ""))
                {
                    // This handles clicks when not dragging
                    if (draggingVoice != v || draggingStep != s)
                    {
                        stepSequencerManager.SetStepActive(v, s, !stepActive);
                    }
                }

                // Draw pitch value (0-12 semitones) inside button
                int semitones = Mathf.RoundToInt(stepPitch * 12f);
                string pitchText = semitones.ToString();
                
                // Use a style for the text
                GUIStyle textStyle = new GUIStyle(GUI.skin.label);
                textStyle.alignment = TextAnchor.MiddleCenter;
                textStyle.normal.textColor = stepActive ? Color.white : Color.grey;
                textStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(actualButtonSize * 0.4f)); // Scale font with button size
                
                GUI.Label(buttonRect, pitchText, textStyle);

                currentX += actualButtonSize + spacing;
            }

            GUI.color = Color.white;
        }

        GUI.EndGroup();

        // Sequence control panel below the step grid
        float controlPanelY = margin + (stepSequencerManager.GetVoices() * (buttonSize + 10f)) + 20f;
        float controlPanelWidth = screenWidth - (margin * 2);
        GUILayout.BeginArea(new Rect(margin, controlPanelY, controlPanelWidth, 200));

        var sequenceList = stepSequencerManager.GetSequenceList();
        int count = sequenceList != null ? sequenceList.Count : 0;

        if (count > 0)
        {
            selectedSequenceIndex = Mathf.Clamp(selectedSequenceIndex, 0, count - 1);
            string[] names = sequenceList.ToArray();
            selectedSequenceIndex = GUILayout.SelectionGrid(selectedSequenceIndex, names, Mathf.Min(count, 8), GUILayout.Height(buttonSize));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Load", GUILayout.Width(80), GUILayout.Height(buttonSize)))
            {
                stepSequencerManager.LoadSequence(sequenceList[selectedSequenceIndex]);
            }
            if (GUILayout.Button("Save", GUILayout.Width(80), GUILayout.Height(buttonSize)))
            {
                stepSequencerManager.SaveSequence(sequenceList[selectedSequenceIndex]);
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(groupSpacing);
        
        // Tempo buttons in horizontal layout to fit all
        GUILayout.BeginHorizontal();

        CreateRequestChangeButton(80,   1);
        CreateRequestChangeButton(100,  4);
        CreateRequestChangeButton(120,  4);
        CreateRequestChangeButton(140,  1);
        CreateRequestChangeButton(160,  3);

        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    public void CreateRequestChangeButton(int bpm, int outro = 1, int transit = 2)
    {
        if (GUILayout.Button($"Request Change {bpm}"))
        {
            stepSequencerManager.RequestBPMTransition(
                    bpm,
                    $"Seq{transit}",
                    $"Seq{outro}");

            GUILayout.Space(spacing);
        }
    }

    #endregion
}