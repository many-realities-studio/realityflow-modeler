using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Ubiq.Messaging;
using Ubiq.Rooms;
using Ubiq.Spawning;
using TransformTypes;
using Microsoft.MixedReality.Toolkit.SpatialManipulation;

public enum CommandType
{
    None,
    Resize,
    Extrude,
    Combine,
    MoveVertices,
    MoveEdges,
    MoveFaces,
    Duplicate
};

public class NetworkedMesh : MonoBehaviour, INetworkSpawnable
{
    public NetworkId NetworkId { get; set; }
    public NetworkContext context;
    private EditableMesh em;
    public bool owner;
    [Tooltip("Displays the owner's UUID and should not be manually changed")]
    public string ownerName;

    // To have access to the owner name
    private SelectTool selectTool;
    public bool isHeld;

    // Select Tool variables
    public bool isSelected;
    private BoundsControl boundsControl;
    private GameObject boundsVisuals;
    private Material meshMaterial;
    private Material boundsMaterial;
    private ObjectManipulator objectManipulator;

    bool lastOwner;
    public bool wasBake;

    private Vector3 lastPosition;
    private Vector3 lastScale;
    private Quaternion lastRotation;
    private float lastSize;

    public bool isDuplicate = false;
    public string originalName = "";
    public bool sourceMesh = false;
    private RoomClient roomClient;

    void Start()
    {
        // Debug.Log("Start is called");
        if (!context.Id.Valid)
            context = NetworkScene.Register(this);
        else
            Debug.Log("ID is already valid");

        // Find the reference for the room client to track peers
        roomClient = NetworkScene.Find(this).GetComponent<RoomClient>();

        roomClient.OnPeerRemoved.AddListener(OnPeerRemoved);

        selectTool = FindObjectOfType<SelectTool>();

        objectManipulator = gameObject.GetComponent<ObjectManipulator>();
        // Find the child game object of this mesh that draws the bounds visuals
        foreach (Transform child in gameObject.transform)
        {
            if (child.gameObject.name.Contains("BoundingBox"))
            {
                boundsVisuals = child.gameObject;
                //Debug.Log("boundsVisuals.name = " + boundsVisuals.name + " in the scene: " + gameObject.transform.parent.parent.parent.name);

                // Find the immediate child game object of the bounding box that contains the instanced material
                foreach (Transform child2 in boundsVisuals.transform)
                {
                    try
                    {
                        boundsMaterial = child2.gameObject.GetComponent<MeshRenderer>().material;
                        // Debug.Log("child2.gameObject.name = " + child2.gameObject.name + " child2.gameObject.GetComponent<MeshRenderer>().material.HasProperty('Color') = "
                        // + child2.gameObject.GetComponent<MeshRenderer>().material.HasProperty("_Color_"));

                        // if (boundsMaterial.HasProperty("_Color_"))
                        // {
                        //     Debug.Log("The current color of bounds visuals for mesh: " + child2.gameObject.name + " in the scene: "
                        //     + child2.gameObject.transform.parent.parent.parent.parent.parent.name + " is the color: " + boundsMaterial.GetColor("_Color_"));
                        // }
                    }
                    catch(Exception e)
                    {
                        Debug.Log(e + " Does " + child2.gameObject.name + " have the ThickerSqueezableBox material?");
                    }
                    break;
                }
            }
        }

        RequestMeshData();
    }

    void OnDestroy()
    {
        roomClient.OnPeerRemoved.RemoveListener(OnPeerRemoved);
    }

    void OnPeerRemoved(IPeer peer)
    {
        Debug.Log("Peer left: " + peer.uuid + " from the mesh name: " + gameObject.name);

        // Deselect mesh if the owner left
        if (ownerName == peer.uuid)
        {
            Debug.Log(peer.uuid + " left so do the operation");
            gameObject.GetComponent<BoundsControl>().HandlesActive = false;
            ControlSelection();
            MeshSelectionManager.Instance.DeselectMesh(gameObject);
        }
    }

    public void RegisterWithSetID(NetworkId netId)
    {
        Debug.Log("calling RegisterWithSetID");
        if (netId.Valid)
            context = NetworkScene.Register(this, netId);

        Debug.Log(context.Id);
    }

    void Awake()
    {
        // Debug.Log("Awake is called");
        em = gameObject.GetComponent<EditableMesh>();
        owner = false;
        isHeld = false;
        isSelected = false;
        lastSize = 0.1f;
        boundsControl = gameObject.GetComponent<BoundsControl>();
        meshMaterial = gameObject.GetComponent<MeshRenderer>().material;
        //boundsControl.HandlesActive = false;

        if (this.NetworkId == null)
            Debug.Log("Networked Object " + gameObject.name + " Network ID is null");
    }

    // Update is called once per frame
    void Update()
    {
        if (owner)
        {
            if (lastPosition != transform.localPosition || lastScale != transform.localScale || lastRotation != transform.localRotation)
            {
                lastPosition = transform.localPosition;
                lastScale = transform.localScale;
                lastRotation = transform.localRotation;
                lastOwner = owner;

                SendTransformData();
            }
        }
    }

    public void SetSize(float newSize)
    {
        lastSize = newSize;
    }


    public bool HasContext()
    {
        return context.Id.Valid;
    }

    public void SetDuplicate(string originalName)
    {
        // Debug.Log("Set Duplicate");
        isDuplicate = true;
        this.originalName = originalName;
    }

    private void RequestMeshData()
    {
        if (!em.isEmpty)
        {
            return;
        }

        // Debug.Log("Mesh is empty");
        // Debug.Log("Requested mesh data");
        Message msg = new Message();
        msg.needsData = true;

        context.SendJson(msg);
    }

    private void BroadcastCreateMesh()
    {
        if(isDuplicate && originalName != "")
        {
            SendDuplicateToolData();
            return;
        }

        context.SendJson(new Message()
        {
            position = transform.localPosition,
            scale = transform.localScale,
            rotation = transform.localRotation,
            size = lastSize,
            shapeType = em.baseShape,

            meshColor = meshMaterial.color,
            meshMetallic = meshMaterial.GetFloat("_Metallic"),
            meshSmoothness = meshMaterial.GetFloat("_Glossiness"),
            boundsColor = new Color(0.078f, 0.54f, 1f, 1f),
            objectManipulator = true
        });

        //SendCachedMeshData();
    }

    private void CreateMesh(ShapeType type)
    {
        PrimitiveData data = PrimitiveGenerator.CreatePrimitive(type);

        if (em)
        {
            em.CreateMesh(data);
        }

        boundsControl.enabled = true;
        //Destroy(mesh.gameObject);
    }

    public void StartHold()
    {
        if ((!owner && isHeld) || gameObject.GetComponent<SelectToolManager>().gizmoTool.isActive)
            return;

        owner = true;
        isHeld = true;
        // Debug.Log("StartHold() was called");
        context.SendJson(new Message()
        {
            position = transform.localPosition,
            scale = transform.localScale,
            rotation = transform.localRotation,
            owner = false,
            isHeld = true,
            isSelected = isSelected,
            handlesActive = boundsControl.HandlesActive,
            boundsVisuals = boundsVisuals.activeInHierarchy,
            meshColor = meshMaterial.color,
            meshMetallic = meshMaterial.GetFloat("_Metallic"),
            meshSmoothness = meshMaterial.GetFloat("_Glossiness"),
            boundsColor = new Color(1f, 0.21f, 0.078f, 1f),
            objectManipulator = false
        });
    }

    public void EndHold()
    {
        if (!gameObject.GetComponent<CacheMeshData>().networkedPlayManager.playMode)
        {
            VertexPosition.BakeVerticesWithNetworking(GetComponent<EditableMesh>());
        }

        if (isSelected || gameObject.GetComponent<SelectToolManager>().gizmoTool.isActive)
            return;

        //Debug.Log("EndHold() was called");
        isHeld = false;
        // Debug.Log("Run the EndHold() networking messages");
        
        context.SendJson(new Message()
        {
            position = transform.localPosition,
            scale = transform.localScale,
            rotation = transform.localRotation,
            owner = false,
            isHeld = false,
            isSelected = isSelected,
            handlesActive = boundsControl.HandlesActive,
            boundsVisuals = boundsVisuals.activeInHierarchy,
            meshColor = meshMaterial.color,
            meshMetallic = meshMaterial.GetFloat("_Metallic"),
            meshSmoothness = meshMaterial.GetFloat("_Glossiness"),
            boundsColor = new Color(0.078f, 0.54f, 1f, 1f),
            objectManipulator = true
        });
    }

    /// <summary>
    /// Used for communicating to the server if the mesh is currently selected or not.
    /// </summary>
    public void ControlSelection()
    {
        //Debug.Log("ControlSelection() was called");
        // If the mesh is selected, then start the selection
        if (gameObject.GetComponent<BoundsControl>().HandlesActive)
        {
            if (!owner && isSelected)
            return;

            //Debug.Log("This mesh is now selected");
            owner = true;
            ownerName = selectTool.ownerName;
            isSelected = true;
            boundsControl.HandlesActive = true;
            boundsVisuals.SetActive(true);
            //objectManipulator.enabled = true;
            context.SendJson(new Message()
            {
                position = transform.localPosition,
                scale = transform.localScale,
                rotation = transform.localRotation,
                owner = false,
                mOwnerName = ownerName,
                isSelected = true,
                handlesActive = true,
                boundsVisuals = true,
                meshColor = meshMaterial.color,
                meshMetallic = meshMaterial.GetFloat("_Metallic"),
                meshSmoothness = meshMaterial.GetFloat("_Glossiness"),
                boundsColor = new Color(1f, 0.21f, 0.078f, 1f),
                objectManipulator = false
            });
        }
        // Else the mesh is not selected, so end the selection
        else
        {
            isSelected = false;
            boundsControl.HandlesActive = false;
            boundsVisuals.SetActive(false);
            context.SendJson(new Message()
            {
                position = transform.localPosition,
                scale = transform.localScale,
                rotation = transform.localRotation,
                owner = false,
                mOwnerName = "",
                isSelected = false,
                handlesActive = false,
                boundsVisuals = false,
                meshColor = meshMaterial.color,
                meshMetallic = meshMaterial.GetFloat("_Metallic"),
                meshSmoothness = meshMaterial.GetFloat("_Glossiness"),
                boundsColor = new Color(0.078f, 0.54f, 1f, 1f),
                objectManipulator = true
            });
        }
    }

    public void SendTransformData()
    {
        // Debug.Log("SendTransformData() was called");

        context.SendJson(new Message()
        {
            position = transform.localPosition,
            scale = transform.localScale,
            rotation = transform.localRotation,
            owner = false,
            isHeld = isHeld,
            isSelected = isSelected,
            handlesActive = boundsControl.HandlesActive,
            boundsVisuals = boundsVisuals.activeInHierarchy,
            meshColor = meshMaterial.color,
            meshMetallic = meshMaterial.GetFloat("_Metallic"),
            meshSmoothness = meshMaterial.GetFloat("_Glossiness"),
            boundsColor = new Color(1f, 0.21f, 0.078f, 1f),
            objectManipulator = wasBake
        });

        wasBake = false;
    }

    public void UpdateAndSendMeshResizeData(float newSize)
    {
        // Debug.Log("SendMeshResizeData() was called");
        lastSize = newSize;
        context.SendJson(new Message()
        {
            type = CommandType.Resize,
            size = lastSize,

            meshColor = meshMaterial.color,
            meshMetallic = meshMaterial.GetFloat("_Metallic"),
            meshSmoothness = meshMaterial.GetFloat("_Glossiness"),
            boundsColor = new Color(0.078f, 0.54f, 1f, 1f),
            objectManipulator = true
        });
    }

    private void ResizeMesh(float newSize)
    {
        lastSize = newSize;
        PrimitiveRebuilder.RebuildMesh(em, lastSize);
    }

    public void SendVertexTransformData(TransformType type, int[] indicies, Vector3 pos, Quaternion rotation, Vector3 scale)
    {
        Vector3[] arr = { pos };
        context.SendJson(new Message()
        {
            type = CommandType.MoveVertices,
            transformType = type,
            selectedIndicies = indicies,
            componentTranslation = arr,
            componentRotation = rotation,
            componentScale = scale,
        });
    }

    private void UpdateVertices(TransformType type, int[] indices, Vector3 pos, Quaternion rotation, Vector3 scale)
    {
        if (type == TransformType.Translate)
        {
            em.TransformVertices(indices, pos);
        }
        else if (type == TransformType.Rotate)
        {
            em.RotateVertices(indices, rotation);
        }
        else if (type == TransformType.Scale)
        {
            em.ScaleVertices(indices, scale);
        }
        else if (type == TransformType.Bake)
        {
            em.BakeVertices();
        }

        if (gameObject.GetComponent<BoundsControl>().HandlesActive)
        {
            gameObject.GetComponent<BoundsControl>().RecomputeBounds();
        }
    }

    public void SendDuplicateToolData()
    {
        if (!context.Id.Valid)
            context = NetworkScene.Register(this);

        context.SendJson(new Message()
        {
            type = CommandType.Duplicate,
            meshToCopy = originalName,
            position = transform.localPosition,
            scale = transform.localScale,
            rotation = transform.localRotation,
            meshColor = meshMaterial.color,
            meshMetallic = meshMaterial.GetFloat("_Metallic"),
            meshSmoothness = meshMaterial.GetFloat("_Glossiness"),
            owner = false,
            isHeld = false,
            isSelected = isSelected,
            handlesActive = boundsControl.HandlesActive,
            boundsVisuals = boundsVisuals.activeInHierarchy,
            boundsColor = new Color(0.078f, 0.54f, 1f, 1f),
            objectManipulator = true
        });
    }

    public void DuplicateMesh(string originalName)
    {
        GameObject go = GameObject.Find(originalName);
        if (go != null)
        {
            EditableMesh copyFrom = go.GetComponent<EditableMesh>();
            em.CreateMesh(copyFrom);
            // em.RecalculateBoundsSafe();
            // em.GetComponent<BoundsControl>().RecomputeBounds();
        }
    }

    public struct Message
    {
        public CommandType type;
        public bool needsData;

        public Vector3 position;
        public Vector3 scale;
        public Quaternion rotation;
        public bool owner;
        public string mOwnerName;
        public bool isHeld;
        public bool isSelected;
        public bool handlesActive;
        public bool boundsVisuals;
        public Color meshColor;
        public float meshMetallic;
        public float meshSmoothness;
        public Color boundsColor;
        public bool objectManipulator;

        public int[] selectedIndicies;
        public TransformType transformType;
        public Vector3[] componentTranslation;
        public Quaternion componentRotation;
        public Vector3 componentScale;

        public ShapeType shapeType;
        public float size;

        public string meshToCopy;
    }

    public void ProcessMessage(ReferenceCountedSceneGraphMessage message)
    {
        var m = message.FromJson<Message>();
        if(m.needsData && !em.isEmpty)
        {
            // Debug.Log("We need data for the mesh from scene " + gameObject.transform.parent.parent.parent.name);
            StartHold();
            BroadcastCreateMesh();
            return;
        }

        if(em.isEmpty && (m.shapeType != ShapeType.NoShape))
        {
            CreateMesh(m.shapeType);
            ResizeMesh(m.size);
            return;
        }

        if(m.type == CommandType.Resize)
        {
            ResizeMesh(m.size);
            return;
        }

        if (m.type == CommandType.MoveVertices)
        {
            UpdateVertices(m.transformType, m.selectedIndicies, m.componentTranslation[0],
                m.componentRotation, m.componentScale);
            return;
        }

        if (m.type == CommandType.Duplicate)
        {
            DuplicateMesh(m.meshToCopy);
        }

        // Update the component
        transform.localPosition = m.position;
        transform.localScale = m.scale;
        transform.localRotation = m.rotation;
        owner = m.owner;
        ownerName = m.mOwnerName;
        isHeld = m.isHeld;
        isSelected = m.isSelected;
        boundsControl.HandlesActive = m.handlesActive;
        boundsVisuals.SetActive(m.boundsVisuals);
        meshMaterial.SetColor("_Color", m.meshColor);
        meshMaterial.SetFloat("_Metallic", m.meshMetallic);
        meshMaterial.SetFloat("_Glossiness", m.meshSmoothness);
        boundsMaterial.SetColor("_Color_", m.boundsColor);
        objectManipulator.enabled = m.objectManipulator;

        // Make sure update doesn't trigger as a result of the message
        lastPosition = transform.localPosition;
        lastScale = transform.localScale;
        lastRotation = transform.localRotation;
        lastOwner = owner;
    }
}
