# Page ZIP import

Download Page exports `project.json` plus per-frame manifests and assets. Import the ZIP in Unity, select the page bundle, and choose **Generate Prefabs (all frames)**.

Every frame is saved as an independent uGUI prefab in the configured output folder. Names are sanitized; duplicate output names within the bundle cause generation to stop before overwriting outputs. Existing prefabs with matching names are replaced on a later import.

Shells and overlays are exported as individual frame assets. The importer does not connect pages, mount them into a scene, or create Canvas/Camera/EventSystem objects in the user's scene. Assemble those relationships in the consuming application.
