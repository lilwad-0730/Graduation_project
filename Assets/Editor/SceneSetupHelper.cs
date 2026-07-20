using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Cinemachine;

public class SceneSetupHelper : EditorWindow
{
    [MenuItem("Tools/Initialize Scene Systems")]
    public static void InitializeSceneSystems()
    {
        var activeScene = EditorSceneManager.GetActiveScene();
        
        Debug.Log($"[SceneSetupHelper] Starting initialization for scene: {activeScene.name}");
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Initialize Game Systems");
        int groupIndex = Undo.GetCurrentGroup();

        // 1. Setup Player
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null)
        {
            playerObj = GameObject.Find("Player");
        }

        bool createdNewPlayer = false;
        if (playerObj == null)
        {
            playerObj = new GameObject("Player");
            playerObj.tag = "Player";
            Undo.RegisterCreatedObjectUndo(playerObj, "Create Player Object");
            createdNewPlayer = true;
            Debug.Log("[SceneSetupHelper] Created new Player GameObject.");
        }
        else
        {
            Undo.RecordObject(playerObj, "Setup Player");
        }

        // Setup Rigidbody
        Rigidbody rb = playerObj.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = playerObj.AddComponent<Rigidbody>();
            Undo.RegisterCreatedObjectUndo(rb, "Add Rigidbody to Player");
        }
        rb.mass = 10f;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 0.05f;
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;

        // Setup BoxCollider
        BoxCollider col = playerObj.GetComponent<BoxCollider>();
        if (col == null)
        {
            col = playerObj.AddComponent<BoxCollider>();
            Undo.RegisterCreatedObjectUndo(col, "Add BoxCollider to Player");
        }
        col.isTrigger = false;
        col.size = new Vector3(1.3387811f, 1.9315275f, 1f);
        col.center = new Vector3(0.19047467f, 0.003233647f, 0f);

        // Find momo model in the scene and make it child
        Transform momoModel = null;
        
        // Search by name
        GameObject momoObj = GameObject.Find("momo Slow Run in place");
        if (momoObj == null)
        {
            // Search by component
            Animator[] animators = FindObjectsByType<Animator>(FindObjectsSortMode.None);
            foreach (var anim in animators)
            {
                if (anim.gameObject != playerObj && anim.transform.parent != playerObj.transform)
                {
                    momoModel = anim.transform;
                    break;
                }
            }
        }
        else
        {
            momoModel = momoObj.transform;
        }

        if (momoModel != null)
        {
            Vector3 originalWorldPos = momoModel.position;
            Quaternion originalWorldRot = momoModel.rotation;

            if (createdNewPlayer)
            {
                playerObj.transform.position = originalWorldPos;
                playerObj.transform.rotation = Quaternion.identity; // 根物件必須維持 (0,0,0) 旋轉以符 2.5D 物理約束
            }

            Undo.SetTransformParent(momoModel, playerObj.transform, "Parent momo to Player");
            momoModel.localPosition = Vector3.zero;
            momoModel.localRotation = originalWorldRot; // 模型繼承原有的視覺旋轉 (如 Y=90度)
            momoModel.localScale = new Vector3(1.14375f, 1.1320312f, 1.5f);
            Debug.Log($"[SceneSetupHelper] Re-parented momo model {momoModel.name} to Player and preserved position.");
        }

        // Setup PlayerMovement script
        PlayerMovement movement = playerObj.GetComponent<PlayerMovement>();
        if (movement == null)
        {
            movement = playerObj.AddComponent<PlayerMovement>();
            Undo.RegisterCreatedObjectUndo(movement, "Add PlayerMovement to Player");
        }
        movement.baseSpeed = 6f;
        movement.pullRange = 1f;
        movement.jumpForce = 10f;
        movement.smoothCameraY = true;
        movement.cameraYDamping = 0.25f;
        movement.fallVelocityThreshold = -3f;
        movement.fallAnimationDelay = 1.8f;
        if (momoModel != null)
        {
            movement.animator = momoModel.GetComponent<Animator>();
        }

        // Setup PlayerRespawnSystem script
        PlayerRespawnSystem respawn = playerObj.GetComponent<PlayerRespawnSystem>();
        if (respawn == null)
        {
            respawn = playerObj.AddComponent<PlayerRespawnSystem>();
            Undo.RegisterCreatedObjectUndo(respawn, "Add PlayerRespawnSystem to Player");
        }
        respawn.failKnockbackDistance = 15f;
        respawn.knockbackVelocityThreshold = -8f;
        respawn.respawnPointTag = "RespawnPoint";
        respawn.cameraOffsetFromPlayer = new Vector3(0, 5, -10);
        respawn.fadeDuration = 1.5f;
        respawn.blackScreenTime = 2.5f;

        // 2. Setup Main Camera
        GameObject mainCamObj = GameObject.FindWithTag("MainCamera");
        if (mainCamObj == null)
        {
            mainCamObj = GameObject.Find("Main Camera");
        }
        if (mainCamObj == null)
        {
            mainCamObj = new GameObject("Main Camera");
            mainCamObj.tag = "MainCamera";
            mainCamObj.AddComponent<Camera>();
            Undo.RegisterCreatedObjectUndo(mainCamObj, "Create Main Camera");
            Debug.Log("[SceneSetupHelper] Created new Main Camera GameObject.");
        }
        else
        {
            Undo.RecordObject(mainCamObj, "Setup Main Camera");
        }
        
        Vector3 camPos = mainCamObj.transform.position;
        camPos.z = -10f;
        mainCamObj.transform.position = camPos;

        SimpleCameraBounds bounds = mainCamObj.GetComponent<SimpleCameraBounds>();
        if (bounds == null)
        {
            bounds = mainCamObj.AddComponent<SimpleCameraBounds>();
            Undo.RegisterCreatedObjectUndo(bounds, "Add SimpleCameraBounds to Main Camera");
        }
        bounds.backgroundTags = new string[] { "Background", "FallingBackground", "RuinedBackground" };
        bounds.clampYAxis = true;

        CinemachineBrain brain = mainCamObj.GetComponent<CinemachineBrain>();
        if (brain == null)
        {
            brain = mainCamObj.AddComponent<CinemachineBrain>();
            Undo.RegisterCreatedObjectUndo(brain, "Add CinemachineBrain to Main Camera");
            Debug.Log("[SceneSetupHelper] Added CinemachineBrain to Main Camera.");
        }

        // 3. Setup CinemachineCamera
        CinemachineCamera vcam = FindFirstObjectByType<CinemachineCamera>();
        if (vcam == null)
        {
            GameObject vcamObj = new GameObject("CinemachineCamera");
            vcamObj.tag = "Camera";
            vcam = vcamObj.AddComponent<CinemachineCamera>();
            Undo.RegisterCreatedObjectUndo(vcamObj, "Create CinemachineCamera");
            Debug.Log("[SceneSetupHelper] Created new CinemachineCamera GameObject.");
        }
        else
        {
            Undo.RecordObject(vcam.gameObject, "Setup CinemachineCamera");
        }

        Vector3 vcamPos = vcam.transform.position;
        vcamPos.z = -10f;
        vcam.transform.position = vcamPos;

        // Use SerializedObject to avoid compilation errors with private fields/internal properties
        SerializedObject soCam = new SerializedObject(vcam);
        SerializedProperty priorityProp = soCam.FindProperty("Priority");
        if (priorityProp != null)
        {
            SerializedProperty valueProp = priorityProp.FindPropertyRelative("m_Value");
            if (valueProp != null) valueProp.intValue = 10;
            SerializedProperty enabledProp = priorityProp.FindPropertyRelative("Enabled");
            if (enabledProp != null) enabledProp.boolValue = true;
        }
        soCam.ApplyModifiedProperties();

        vcam.Lens.FieldOfView = 34;
        vcam.Lens.OrthographicSize = 7;
        vcam.Lens.NearClipPlane = 0.3f;
        vcam.Lens.FarClipPlane = 1000;
        vcam.Lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
        vcam.Follow = playerObj.transform;

        // Add CinemachineFollow component
        CinemachineFollow followComponent = vcam.GetComponent<CinemachineFollow>();
        if (followComponent == null)
        {
            followComponent = vcam.gameObject.AddComponent<CinemachineFollow>();
            Undo.RegisterCreatedObjectUndo(followComponent, "Add CinemachineFollow");
        }
        
        SerializedObject soFollow = new SerializedObject(followComponent);
        SerializedProperty trackerSettingsProp = soFollow.FindProperty("TrackerSettings");
        if (trackerSettingsProp != null)
        {
            SerializedProperty bindingModeProp = trackerSettingsProp.FindPropertyRelative("BindingMode");
            if (bindingModeProp != null) bindingModeProp.intValue = 4; // 4 is LockToTargetWithWorldUp
            SerializedProperty positionDampingProp = trackerSettingsProp.FindPropertyRelative("PositionDamping");
            if (positionDampingProp != null) positionDampingProp.vector3Value = new Vector3(0.1f, 0.1f, 0.1f);
        }
        SerializedProperty followOffsetProp = soFollow.FindProperty("FollowOffset");
        if (followOffsetProp != null)
        {
            followOffsetProp.vector3Value = new Vector3(0f, 0f, -10f);
        }
        soFollow.ApplyModifiedProperties();

        // 4. Setup Default RespawnPoint
        GameObject respawnPoint = GameObject.FindWithTag("RespawnPoint");
        if (respawnPoint == null)
        {
            respawnPoint = new GameObject("DefaultRespawnPoint");
            respawnPoint.tag = "RespawnPoint";
            respawnPoint.transform.position = playerObj.transform.position;
            Undo.RegisterCreatedObjectUndo(respawnPoint, "Create Default RespawnPoint");
            Debug.Log("[SceneSetupHelper] Created Default RespawnPoint at Player's position.");
        }

        // 5. Setup AudioManager
        AudioManager audioManager = FindFirstObjectByType<AudioManager>();
        if (audioManager == null)
        {
            GameObject audioManagerObj = new GameObject("AudioManager");
            audioManager = audioManagerObj.AddComponent<AudioManager>();
            Undo.RegisterCreatedObjectUndo(audioManagerObj, "Create AudioManager");
            Debug.Log("[SceneSetupHelper] Created AudioManager GameObject.");
        }

        // Collapse undo group
        Undo.CollapseUndoOperations(groupIndex);

        EditorSceneManager.MarkSceneDirty(activeScene);
        Debug.Log("[SceneSetupHelper] Scene systems initialized successfully!");
        
        EditorUtility.DisplayDialog("Scene Initializer", 
            $"Successfully initialized systems in scene '{activeScene.name}'!\n\n" +
            "1. Setup Player (Rigidbody, Collider, PlayerMovement, PlayerRespawnSystem)\n" +
            "2. Connected momo model as Player child\n" +
            "3. Added SimpleCameraBounds to Main Camera\n" +
            "4. Created/Configured CinemachineCamera with Orthographic follow\n" +
            "5. Created Default RespawnPoint\n" +
            "6. Created AudioManager\n\n" +
            "Remember to tag your background Object as 'Background' with a BoxCollider for camera bounding!", 
            "Awesome");
    }
}
