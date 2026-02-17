using System.Collections;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using Unity.EditorCoroutines.Editor;
#endif
using UnityEngine;
using UnityEngine.Events;

public class LevelGenerator : MonoBehaviour
{
	public static LevelGenerator Instance;


    // "Global" references
    public WalkerManager WalkerManager;
	[HideInInspector]
    public LevelBoundary ActiveBoundary;
	[Header("Debug")]
	public GridNetworkPlayer NetworkPlayer;
	[Header("Grid Size")]
	public int GridWidth = 1000;
	public int GridHeight = 1000; 
	public float CellSize = 3;

	[Header("Level Generation Pipeline")]
	public List<LevelGenerationStep> Steps;
	public List<Room> AllRooms;

	/** EVENTS **/
	public UnityEvent OnLevelCreated;

	private GenerationContext context;

    void Awake()
	{
		Instance = this;
		OnLevelCreated = new UnityEvent();
		AllRooms = new List<Room>();
		WalkerManager = GetComponent<WalkerManager>();
	}

	void Start()
	{
		transform.position = new Vector3(CellSize / 2, 0, CellSize / 2);
		StartCoroutine(CreateLevel());
	}

	public void ClearLevel()
	{
		var tempList = transform.Cast<Transform>().ToList();
		foreach(Transform child in tempList)
		{
			if(Application.isEditor)
			{
				DestroyImmediate(child.gameObject);			
			}
			else
			{
				Destroy(child.gameObject);
			}
		}
	}


	#if UNITY_EDITOR
	public void CreateLevelInEditor()
	{
		ClearLevel();
		EditorCoroutineUtility.StartCoroutineOwnerless(CreateLevel());
	}
	#endif

	IEnumerator CreateLevel()
	{
		ClearLevel();
		Initialize();

		foreach(var step in Steps)
		{
			yield return step.Execute(context);
		}

		if (NetworkPlayer != null)
			NetworkPlayer.Setup(context);

		OnLevelCreated.Invoke();
	}

	void Initialize()
	{
		var grid = new Grid(GridWidth, GridHeight, CellSize);
		var levelData = new LevelData();
		context = new(grid, this, levelData);
	}
}
