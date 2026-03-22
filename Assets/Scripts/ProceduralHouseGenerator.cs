using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class ProceduralHouseGenerator : MonoBehaviour
{
    private const string TargetSceneName = "Dungeon Test";
    private const string ResourceFolder = "Dungeon Parts/";
    private const float CellSize = 2.5f;
    private const float HalfCellSize = CellSize * 0.5f;
    private const float FloorHeight = 2.5f;
    private const float InitialDoorDistance = 10f;
    private const float BoundaryDepth = 0.2f;
    private const float VisibilityProbeSize = 0.15f;
    private const float VisibilityVerticalOffset = 1.2f;
    private const float VisibilityLateralOffset = 0.45f;
    private const float MinimumHiddenGenerationDistance = CellSize * 3.25f;
    private const float EntranceActivationDistance = 0.95f;
    private const float EntranceCenterTolerance = 0.35f;
    private const float EntrancePassThroughDepth = 0.45f;
    private const float WalkingStartDistance = 0.1f;
    private const float VerticalWalkingTolerance = 0.05f;
    private const int RequiredSegmentsAhead = 4;
    private const int MaxGenerationAttemptsPerType = 12;
    private const int MaxLevels = 3;
    private const bool UseExteriorWindows = false;
    private const int InitialStraightCells = 1;
    private const int InitialTurnCells = 3;
    private const float TurningHallwayChance = 0.75f;
    private const float RoomWingChance = 0.55f;

    private static readonly Direction[] AllDirections =
    {
        Direction.North,
        Direction.East,
        Direction.South,
        Direction.West
    };

    private readonly Dictionary<GridCoord, CellData> cells = new Dictionary<GridCoord, CellData>();
    private readonly Dictionary<BoundaryKey, GameObject> boundaryObjects = new Dictionary<BoundaryKey, GameObject>();
    private readonly Dictionary<BoundaryKey, BoundaryVisualType> boundaryVisuals = new Dictionary<BoundaryKey, BoundaryVisualType>();
    private readonly HashSet<BoundaryKey> doorBoundaries = new HashSet<BoundaryKey>();
    private readonly HashSet<BoundaryKey> openBoundaries = new HashSet<BoundaryKey>();
    private readonly List<SegmentData> segments = new List<SegmentData>();
    private readonly List<BoundaryKey> boundaryRemovalBuffer = new List<BoundaryKey>();

    private GameObject floorPrefab;
    private GameObject wallPrefab;
    private GameObject ceilingPrefab;
    private GameObject doorWallPrefab;
    private GameObject windowWallPrefab;
    private GameObject stairsPrefab;

    private System.Random random;
    private Transform playerTransform;
    private Camera playerCamera;
    private CharacterController playerController;
    private Vector3 worldGridOrigin;
    private Direction initialDirection;
    private Frontier tailFrontier;
    private int sessionSeed;
    private int currentSegmentIndex;
    private Vector3 previousPlayerPosition;
    private float groundedWalkDistance;
    private bool initialized;
    private bool generationStarted;
    private GameObject previewEntranceDoor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !string.Equals(scene.name, TargetSceneName, StringComparison.OrdinalIgnoreCase))
            return;

        if (FindObjectOfType<ProceduralHouseGenerator>() != null)
            return;

        GameObject generatorObject = new GameObject("Procedural House Generator");
        generatorObject.AddComponent<ProceduralHouseGenerator>();
    }

    private void Start()
    {
        if (!TryInitialize())
        {
            Destroy(gameObject);
            return;
        }

        ConfigureTestingLighting();
        initialized = true;
        previousPlayerPosition = playerTransform != null ? playerTransform.position : Vector3.zero;
    }

    private void Update()
    {
        if (!initialized)
            return;

        ResolvePlayerReferences();

        if (playerTransform == null || playerCamera == null)
            return;

        if (!generationStarted)
        {
            if (CanBeginGeneration())
                BeginGeneration();

            previousPlayerPosition = playerTransform.position;
            return;
        }

        currentSegmentIndex = FindCurrentSegmentIndex(playerTransform.position);
        EnsureForwardBuffer(false);
        previousPlayerPosition = playerTransform.position;
    }

    private bool TryInitialize()
    {
        if (!LoadPrefabs())
            return false;

        ResolvePlayerReferences();
        if (playerTransform == null)
            return false;

        sessionSeed = unchecked((int)DateTime.UtcNow.Ticks);
        random = new System.Random(sessionSeed);
        initialDirection = GetClosestDirection(playerTransform.forward);
        worldGridOrigin = ComputeInitialOrigin(playerTransform.position, initialDirection);
        SpawnPreviewEntranceDoor();
        return true;
    }

    private bool LoadPrefabs()
    {
        floorPrefab = Resources.Load<GameObject>(ResourceFolder + "Floor");
        wallPrefab = Resources.Load<GameObject>(ResourceFolder + "Wall");
        ceilingPrefab = Resources.Load<GameObject>(ResourceFolder + "Ceiling");
        doorWallPrefab = Resources.Load<GameObject>(ResourceFolder + "DoorWall");
        windowWallPrefab = Resources.Load<GameObject>(ResourceFolder + "WindowWall");
        stairsPrefab = Resources.Load<GameObject>(ResourceFolder + "Stairs");

        return floorPrefab != null &&
               wallPrefab != null &&
               ceilingPrefab != null &&
               doorWallPrefab != null &&
               windowWallPrefab != null &&
               stairsPrefab != null;
    }

    private void ResolvePlayerReferences()
    {
        if (playerTransform == null)
        {
            SimpleFirstPersonController controller = FindObjectOfType<SimpleFirstPersonController>();
            if (controller != null)
                playerTransform = controller.transform;
        }

        if (playerController == null && playerTransform != null)
            playerController = playerTransform.GetComponent<CharacterController>();

        if (playerCamera == null)
            playerCamera = Camera.main;

        if (playerCamera == null && playerTransform != null)
            playerCamera = playerTransform.GetComponentInChildren<Camera>();
    }

    private void ConfigureTestingLighting()
    {
        RenderSettings.fog = false;
        RenderSettings.skybox = null;
        RenderSettings.sun = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.white;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.reflectionIntensity = 0f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        QualitySettings.shadows = ShadowQuality.Disable;
        QualitySettings.shadowDistance = 0f;
        QualitySettings.pixelLightCount = 0;

        Light[] sceneLights = FindObjectsOfType<Light>(true);
        for (int i = 0; i < sceneLights.Length; i++)
        {
            sceneLights[i].shadows = LightShadows.None;
            sceneLights[i].enabled = false;
        }

        DynamicGI.UpdateEnvironment();
    }

    private void BeginGeneration()
    {
        if (generationStarted)
            return;

        if (!TryBuildInitialHall(out SegmentBlueprint initialBlueprint))
            return;

        if (previewEntranceDoor != null)
        {
            Destroy(previewEntranceDoor);
            previewEntranceDoor = null;
        }

        SegmentData initialSegment = CommitBlueprint(initialBlueprint);
        segments.Add(initialSegment);
        currentSegmentIndex = 0;

        doorBoundaries.Add(GetCanonicalBoundary(initialBlueprint.EntranceDoorCell, initialBlueprint.EntranceDoorSide));
        tailFrontier = initialBlueprint.ExitFrontier;
        generationStarted = true;

        RebuildAllBoundaries();
        EnsureForwardBuffer(false);
    }

    private void SpawnPreviewEntranceDoor()
    {
        if (previewEntranceDoor != null)
            Destroy(previewEntranceDoor);

        Vector3 position = GetBoundaryWorldPosition(GridCoord.Zero, Opposite(initialDirection));
        Quaternion rotation = Quaternion.Euler(0f, GetDirectionYaw(Opposite(initialDirection)), 0f);
        previewEntranceDoor = Instantiate(doorWallPrefab, position, rotation, transform);
        previewEntranceDoor.name = "Generated Entrance Preview";
    }

    private bool HasPlayerStartedWalking()
    {
        if (playerTransform == null)
            return false;

        Vector3 delta = playerTransform.position - previousPlayerPosition;
        float horizontalDistance = new Vector2(delta.x, delta.z).magnitude;
        bool grounded = playerController == null || playerController.isGrounded;
        bool lowVerticalMotion = Mathf.Abs(delta.y) <= VerticalWalkingTolerance;

        if (grounded && lowVerticalMotion && horizontalDistance > 0.0005f)
            groundedWalkDistance += horizontalDistance;
        else if (!grounded)
            groundedWalkDistance = 0f;

        return groundedWalkDistance >= WalkingStartDistance;
    }

    private bool CanBeginGeneration()
    {
        if (!HasPlayerStartedWalking())
            return false;

        if (previewEntranceDoor == null || playerTransform == null)
            return true;

        Vector3 playerPosition = playerTransform.position;
        Vector3 entrancePosition = previewEntranceDoor.transform.position;
        Vector2 horizontalDelta = new Vector2(playerPosition.x - entrancePosition.x, playerPosition.z - entrancePosition.z);
        if (horizontalDelta.magnitude > EntranceActivationDistance)
            return false;

        Vector3 entranceForward = GetDirectionVector(initialDirection);
        Vector3 entranceRight = GetDirectionVector(TurnRight(initialDirection));
        Vector3 entranceToPlayer = new Vector3(
            playerPosition.x - entrancePosition.x,
            0f,
            playerPosition.z - entrancePosition.z);

        float entranceDepth = Vector3.Dot(entranceToPlayer, entranceForward);
        float lateralOffset = Mathf.Abs(Vector3.Dot(entranceToPlayer, entranceRight));

        bool centeredApproachTrigger =
            lateralOffset <= EntranceCenterTolerance &&
            entranceDepth >= -EntranceActivationDistance &&
            entranceDepth <= 0.15f;

        bool insideFallbackTrigger = entranceDepth >= EntrancePassThroughDepth;
        return centeredApproachTrigger || insideFallbackTrigger;
    }

    private void EnsureForwardBuffer(bool ignoreVisibility)
    {
        int attempts = 0;
        while (segments.Count - 1 - currentSegmentIndex < RequiredSegmentsAhead && attempts < RequiredSegmentsAhead + 2)
        {
            if (!TryExtendPath(ignoreVisibility))
                break;

            attempts++;
        }
    }

    private bool TryExtendPath(bool ignoreVisibility)
    {
        BoundaryKey tailBoundary = GetCanonicalBoundary(tailFrontier.SourceCell, tailFrontier.Direction);
        if (!CanGenerateAtBoundary(tailBoundary, ignoreVisibility))
            return false;

        if (!TryBuildNextSegment(tailFrontier, out SegmentBlueprint blueprint))
            return false;

        BoundaryKey connectionBoundary = GetCanonicalBoundary(tailFrontier.SourceCell, tailFrontier.Direction);
        if (blueprint.UsesOpenEntrance)
            openBoundaries.Add(connectionBoundary);
        else
            doorBoundaries.Add(connectionBoundary);

        for (int i = 0; i < blueprint.OpenBoundaryCount; i++)
            openBoundaries.Add(blueprint.OpenBoundaries[i]);

        SegmentData segment = CommitBlueprint(blueprint);
        segments.Add(segment);
        tailFrontier = blueprint.ExitFrontier;
        RebuildAllBoundaries();
        return true;
    }

    private bool CanGenerateAtBoundary(BoundaryKey boundary, bool ignoreVisibility)
    {
        if (ignoreVisibility)
            return true;

        Transform referenceTransform = playerCamera != null ? playerCamera.transform : playerTransform;
        if (referenceTransform != null)
        {
            Vector3 boundaryPosition = GetBoundaryWorldPosition(boundary.Cell, boundary.Side);
            Vector3 referencePosition = referenceTransform.position;
            Vector2 horizontalDelta = new Vector2(boundaryPosition.x - referencePosition.x, boundaryPosition.z - referencePosition.z);
            if (horizontalDelta.magnitude < MinimumHiddenGenerationDistance)
                return false;
        }

        return IsBoundaryHidden(boundary);
    }

    private SegmentType[] BuildTypeOrder()
    {
        bool canUseStairs = tailFrontier.SourceCell.Level < MaxLevels - 1;
        List<SegmentType> types = new List<SegmentType>(3)
        {
            SegmentType.Room,
            SegmentType.Hallway
        };

        if (canUseStairs)
            types.Add(SegmentType.Staircase);

        Shuffle(types);
        return types.ToArray();
    }

    private bool TryBuildInitialHall(out SegmentBlueprint blueprint)
    {
        GridCoord entranceCell = GridCoord.Zero;
        Direction turnDirection = random.Next(2) == 0
            ? TurnLeft(initialDirection)
            : TurnRight(initialDirection);

        SegmentBlueprint candidate = new SegmentBlueprint
        {
            Type = SegmentType.Hallway,
            EntranceDoorCell = entranceCell,
            EntranceDoorSide = Opposite(initialDirection),
            UsesOpenEntrance = false
        };

        GridCoord current = entranceCell;
        candidate.AddCell(new CellSpec(current, CellKind.Standard, initialDirection));

        for (int i = 1; i < InitialStraightCells; i++)
        {
            current = current.Step(initialDirection);
            candidate.AddCell(new CellSpec(current, CellKind.Standard, initialDirection));
        }

        for (int i = 0; i < InitialTurnCells; i++)
        {
            current = current.Step(turnDirection);
            candidate.AddCell(new CellSpec(current, CellKind.Standard, turnDirection));
        }

        Frontier frontier = new Frontier(candidate.Cells[candidate.CellCount - 1].Coord, turnDirection);
        if (!IsBlueprintValid(candidate, frontier))
        {
            blueprint = default;
            return false;
        }

        candidate.ExitFrontier = frontier;
        blueprint = candidate;
        return true;
    }

    private bool TryBuildInitialRoom(out SegmentBlueprint blueprint)
    {
        GridCoord entranceCell = GridCoord.Zero;
        for (int attempt = 0; attempt < MaxGenerationAttemptsPerType; attempt++)
        {
            if (TryBuildRoomBlueprint(entranceCell, initialDirection, out blueprint))
            {
                blueprint.EntranceDoorCell = entranceCell;
                blueprint.EntranceDoorSide = Opposite(initialDirection);
                return true;
            }
        }

        blueprint = default;
        return false;
    }

    private bool TryBuildNextSegment(Frontier frontier, out SegmentBlueprint blueprint)
    {
        SegmentType[] types = BuildTypeOrder();
        for (int i = 0; i < types.Length; i++)
        {
            for (int attempt = 0; attempt < MaxGenerationAttemptsPerType; attempt++)
            {
                switch (types[i])
                {
                    case SegmentType.Room:
                        if (TryBuildRoomBlueprint(frontier.SourceCell.Step(frontier.Direction), frontier.Direction, out blueprint))
                            return true;
                        break;
                    case SegmentType.Hallway:
                        if (TryBuildHallwayBlueprint(frontier.SourceCell.Step(frontier.Direction), frontier.Direction, out blueprint))
                            return true;
                        break;
                    case SegmentType.Staircase:
                        if (TryBuildStaircaseBlueprint(frontier.SourceCell.Step(frontier.Direction), frontier.Direction, out blueprint))
                            return true;
                        break;
                }
            }
        }

        blueprint = default;
        return false;
    }

    private bool TryBuildRoomBlueprint(GridCoord entranceCell, Direction entryDirection, out SegmentBlueprint blueprint)
    {
        int width = random.Next(3, 6);
        int depth = random.Next(3, 6);
        int leftCells = random.Next(0, width);
        int rightCells = width - 1 - leftCells;

        SegmentBlueprint candidate = new SegmentBlueprint
        {
            Type = SegmentType.Room,
            EntranceDoorCell = entranceCell,
            EntranceDoorSide = Opposite(entryDirection),
            UsesOpenEntrance = false
        };

        for (int forward = 0; forward < depth; forward++)
        {
            for (int lateral = -leftCells; lateral <= rightCells; lateral++)
            {
                GridCoord coord = OffsetFromEntrance(entranceCell, entryDirection, forward, lateral);
                candidate.AddCell(new CellSpec(coord, CellKind.Standard, entryDirection));
            }
        }

        TryAddRoomWing(candidate, entranceCell, entryDirection, depth, leftCells, rightCells);

        Direction[] exitChoices = BuildRoomExitChoices(entryDirection);
        Shuffle(exitChoices);

        for (int i = 0; i < exitChoices.Length; i++)
        {
            Frontier frontier = CreateRoomExit(entranceCell, entryDirection, depth, leftCells, rightCells, exitChoices[i]);
            if (!IsBlueprintValid(candidate, frontier))
                continue;

            candidate.ExitFrontier = frontier;
            blueprint = candidate;
            return true;
        }

        blueprint = default;
        return false;
    }

    private void TryAddRoomWing(
        SegmentBlueprint candidate,
        GridCoord entranceCell,
        Direction entryDirection,
        int depth,
        int leftCells,
        int rightCells)
    {
        if (random.NextDouble() > RoomWingChance)
            return;

        Direction wingDirection = random.Next(2) == 0
            ? TurnLeft(entryDirection)
            : TurnRight(entryDirection);

        int forward = depth > 1 ? random.Next(1, depth) : 0;
        int lateral = wingDirection == TurnLeft(entryDirection) ? -leftCells : rightCells;
        int wingLength = random.Next(1, 3);

        GridCoord current = OffsetFromEntrance(entranceCell, entryDirection, forward, lateral);
        for (int i = 0; i < wingLength; i++)
        {
            current = current.Step(wingDirection);
            candidate.AddCell(new CellSpec(current, CellKind.Standard, wingDirection));
        }
    }

    private bool TryBuildHallwayBlueprint(GridCoord entranceCell, Direction entryDirection, out SegmentBlueprint blueprint)
    {
        SegmentBlueprint candidate = new SegmentBlueprint
        {
            Type = SegmentType.Hallway,
            EntranceDoorCell = entranceCell,
            EntranceDoorSide = Opposite(entryDirection),
            UsesOpenEntrance = false
        };

        GridCoord current = entranceCell;
        Direction currentDirection = entryDirection;
        candidate.AddCell(new CellSpec(current, CellKind.Standard, currentDirection));

        int straightLength = random.Next(2, 4);
        for (int i = 1; i < straightLength; i++)
        {
            current = current.Step(currentDirection);
            candidate.AddCell(new CellSpec(current, CellKind.Standard, currentDirection));
        }

        if (random.NextDouble() < TurningHallwayChance)
        {
            currentDirection = random.Next(2) == 0
                ? TurnLeft(entryDirection)
                : TurnRight(entryDirection);

            int turnLength = random.Next(1, 4);
            for (int i = 0; i < turnLength; i++)
            {
                current = current.Step(currentDirection);
                candidate.AddCell(new CellSpec(current, CellKind.Standard, currentDirection));
            }
        }
        else
        {
            int extraLength = random.Next(0, 3);
            for (int i = 0; i < extraLength; i++)
            {
                current = current.Step(currentDirection);
                candidate.AddCell(new CellSpec(current, CellKind.Standard, currentDirection));
            }
        }

        Frontier frontier = new Frontier(candidate.Cells[candidate.CellCount - 1].Coord, currentDirection);
        if (!IsBlueprintValid(candidate, frontier))
        {
            blueprint = default;
            return false;
        }

        candidate.ExitFrontier = frontier;
        blueprint = candidate;
        return true;
    }

    private bool TryBuildStaircaseBlueprint(GridCoord entranceCell, Direction entryDirection, out SegmentBlueprint blueprint)
    {
        if (entranceCell.Level >= MaxLevels - 1)
        {
            blueprint = default;
            return false;
        }

        GridCoord landingCell = entranceCell.Step(entryDirection).WithLevel(entranceCell.Level + 1);
        if (cells.ContainsKey(entranceCell) || cells.ContainsKey(landingCell))
        {
            blueprint = default;
            return false;
        }

        SegmentBlueprint candidate = new SegmentBlueprint
        {
            Type = SegmentType.Staircase,
            EntranceDoorCell = entranceCell,
            EntranceDoorSide = Opposite(entryDirection),
            UsesOpenEntrance = true
        };

        candidate.AddCell(new CellSpec(entranceCell, CellKind.StairBase, entryDirection));
        candidate.AddCell(new CellSpec(landingCell, CellKind.StairLanding, entryDirection));
        candidate.AddOpenBoundary(GetCanonicalBoundary(landingCell, Opposite(entryDirection)));

        Direction[] exitChoices =
        {
            entryDirection,
            TurnLeft(entryDirection),
            TurnRight(entryDirection)
        };

        Shuffle(exitChoices);
        for (int i = 0; i < exitChoices.Length; i++)
        {
            Frontier frontier = new Frontier(landingCell, exitChoices[i]);
            if (!IsBlueprintValid(candidate, frontier))
                continue;

            candidate.ExitFrontier = frontier;
            blueprint = candidate;
            return true;
        }

        blueprint = default;
        return false;
    }

    private SegmentData CommitBlueprint(SegmentBlueprint blueprint)
    {
        GameObject segmentObject = new GameObject($"Generated Segment {segments.Count:D2} {blueprint.Type}");
        segmentObject.transform.SetParent(transform, false);

        SegmentData segment = new SegmentData
        {
            Index = segments.Count,
            Type = blueprint.Type,
            Root = segmentObject.transform
        };

        bool hasBounds = false;
        Bounds segmentBounds = default;

        for (int i = 0; i < blueprint.CellCount; i++)
        {
            CellSpec spec = blueprint.Cells[i];
            CellData cell = CreateCell(segment, spec);
            cells.Add(spec.Coord, cell);
            segment.Cells.Add(spec.Coord);

            Bounds cellBounds = GetCellBounds(spec);
            if (!hasBounds)
            {
                segmentBounds = cellBounds;
                hasBounds = true;
            }
            else
            {
                segmentBounds.Encapsulate(cellBounds);
            }
        }

        for (int i = 0; i < blueprint.CellCount; i++)
        {
            GridCoord cell = blueprint.Cells[i].Coord;
            for (int directionIndex = 0; directionIndex < AllDirections.Length; directionIndex++)
            {
                Direction direction = AllDirections[directionIndex];
                if (!blueprint.Contains(cell.Step(direction)))
                    continue;

                openBoundaries.Add(GetCanonicalBoundary(cell, direction));
            }
        }

        segment.WorldBounds = segmentBounds;
        segment.ExitFrontier = blueprint.ExitFrontier;
        return segment;
    }

    private CellData CreateCell(SegmentData segment, CellSpec spec)
    {
        Vector3 worldPosition = GetCellWorldPosition(spec.Coord);
        GameObject cellRoot = new GameObject($"{spec.Kind}_{spec.Coord.X}_{spec.Coord.Level}_{spec.Coord.Z}");
        cellRoot.transform.SetParent(segment.Root, false);
        cellRoot.transform.position = worldPosition;

        Instantiate(floorPrefab, worldPosition, Quaternion.identity, cellRoot.transform);

        if (spec.Kind == CellKind.StairBase)
        {
            Instantiate(stairsPrefab, worldPosition, Quaternion.Euler(0f, GetDirectionYaw(spec.PrimaryDirection), 0f), cellRoot.transform);
        }
        else
        {
            GameObject ceilingInstance = Instantiate(ceilingPrefab, worldPosition + Vector3.up * FloorHeight, Quaternion.identity, cellRoot.transform);
            DisableAllColliders(ceilingInstance);
        }

        return new CellData
        {
            Coord = spec.Coord,
            Kind = spec.Kind,
            PrimaryDirection = spec.PrimaryDirection,
            Root = cellRoot,
            SegmentIndex = segment.Index
        };
    }

    private static void DisableAllColliders(GameObject root)
    {
        if (root == null)
            return;

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;
    }

    private bool IsBlueprintValid(SegmentBlueprint blueprint, Frontier exitFrontier)
    {
        if (!blueprint.Contains(exitFrontier.SourceCell))
            return false;

        GridCoord nextCell = exitFrontier.SourceCell.Step(exitFrontier.Direction);
        if (cells.ContainsKey(nextCell) || blueprint.Contains(nextCell))
            return false;

        for (int i = 0; i < blueprint.CellCount; i++)
        {
            if (cells.ContainsKey(blueprint.Cells[i].Coord))
                return false;
        }

        return true;
    }

    private int FindCurrentSegmentIndex(Vector3 playerPosition)
    {
        for (int i = segments.Count - 1; i >= 0; i--)
        {
            Bounds expanded = segments[i].WorldBounds;
            expanded.Expand(new Vector3(0.2f, 1f, 0.2f));
            if (expanded.Contains(playerPosition))
                return i;
        }

        int closestIndex = currentSegmentIndex;
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < segments.Count; i++)
        {
            Vector3 point = segments[i].WorldBounds.ClosestPoint(playerPosition);
            float sqrDistance = (point - playerPosition).sqrMagnitude;
            if (sqrDistance >= closestDistance)
                continue;

            closestDistance = sqrDistance;
            closestIndex = i;
        }

        return closestIndex;
    }

    private void RebuildAllBoundaries()
    {
        HashSet<BoundaryKey> activeKeys = new HashSet<BoundaryKey>();
        foreach (KeyValuePair<GridCoord, CellData> pair in cells)
        {
            for (int i = 0; i < AllDirections.Length; i++)
                activeKeys.Add(GetCanonicalBoundary(pair.Key, AllDirections[i]));
        }

        foreach (BoundaryKey key in activeKeys)
            UpdateBoundary(key, ResolveBoundaryVisual(key));

        boundaryRemovalBuffer.Clear();
        foreach (KeyValuePair<BoundaryKey, GameObject> pair in boundaryObjects)
        {
            if (activeKeys.Contains(pair.Key))
                continue;

            boundaryRemovalBuffer.Add(pair.Key);
        }

        for (int i = 0; i < boundaryRemovalBuffer.Count; i++)
        {
            BoundaryKey key = boundaryRemovalBuffer[i];
            if (boundaryObjects.TryGetValue(key, out GameObject boundaryObject) && boundaryObject != null)
                Destroy(boundaryObject);

            boundaryObjects.Remove(key);
            boundaryVisuals.Remove(key);
        }
    }

    private void UpdateBoundary(BoundaryKey key, BoundaryVisualType type)
    {
        if (type == BoundaryVisualType.None)
        {
            if (boundaryObjects.TryGetValue(key, out GameObject existingNone) && existingNone != null)
                Destroy(existingNone);

            boundaryObjects.Remove(key);
            boundaryVisuals.Remove(key);
            return;
        }

        if (boundaryObjects.TryGetValue(key, out GameObject existing) &&
            existing != null &&
            boundaryVisuals.TryGetValue(key, out BoundaryVisualType existingType) &&
            existingType == type)
        {
            return;
        }

        if (existing != null)
            Destroy(existing);

        GameObject prefab = GetBoundaryPrefab(type);
        if (prefab == null)
        {
            boundaryObjects.Remove(key);
            boundaryVisuals.Remove(key);
            return;
        }

        Vector3 position = GetBoundaryWorldPosition(key.Cell, key.Side);
        Quaternion rotation = Quaternion.Euler(0f, GetDirectionYaw(key.Side), 0f);
        GameObject boundaryObject = Instantiate(prefab, position, rotation, transform);
        boundaryObject.name = $"{type}_{key.Cell.X}_{key.Cell.Level}_{key.Cell.Z}_{key.Side}";
        boundaryObjects[key] = boundaryObject;
        boundaryVisuals[key] = type;
    }

    private BoundaryVisualType ResolveBoundaryVisual(BoundaryKey key)
    {
        bool hasA = cells.TryGetValue(key.Cell, out CellData cellA);
        GridCoord neighbor = key.Cell.Step(key.Side);
        bool hasB = cells.TryGetValue(neighbor, out CellData cellB);
        Direction sideA = key.Side;
        Direction sideB = Opposite(key.Side);

        if (!hasA && !hasB)
            return BoundaryVisualType.None;

        if ((hasA && cellA.SuppressesBoundary(sideA)) || (hasB && cellB.SuppressesBoundary(sideB)))
            return BoundaryVisualType.None;

        if (openBoundaries.Contains(key))
            return BoundaryVisualType.None;

        if (doorBoundaries.Contains(key))
            return BoundaryVisualType.DoorWall;

        if (hasA && hasB)
            return BoundaryVisualType.Wall;

        CellData exteriorCell = hasA ? cellA : cellB;
        Direction exteriorSide = hasA ? sideA : sideB;
        if (ShouldUseWindow(exteriorCell, exteriorSide))
            return BoundaryVisualType.WindowWall;

        return BoundaryVisualType.Wall;
    }

    private bool ShouldUseWindow(CellData cell, Direction side)
    {
        if (!UseExteriorWindows)
            return false;

        if (cell.Kind != CellKind.Standard)
            return false;

        if (cell.SegmentIndex < 0 || cell.SegmentIndex >= segments.Count)
            return false;

        if (segments[cell.SegmentIndex].Type != SegmentType.Room)
            return false;

        int hash = sessionSeed;
        hash = (hash * 397) ^ cell.Coord.X;
        hash = (hash * 397) ^ cell.Coord.Level;
        hash = (hash * 397) ^ cell.Coord.Z;
        hash = (hash * 397) ^ (int)side;
        hash &= int.MaxValue;
        return (hash % 100) < 22;
    }

    private bool IsBoundaryHidden(BoundaryKey boundary)
    {
        if (playerCamera == null)
            return false;

        Bounds bounds = GetBoundaryBounds(boundary);
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(playerCamera);
        if (!GeometryUtility.TestPlanesAABB(planes, bounds))
            return true;

        Vector3[] samplePoints = GetBoundarySamplePoints(boundary);
        GameObject boundaryObject = boundaryObjects.TryGetValue(boundary, out GameObject existing)
            ? existing
            : null;

        for (int i = 0; i < samplePoints.Length; i++)
        {
            Bounds sampleBounds = new Bounds(samplePoints[i], Vector3.one * VisibilityProbeSize);
            if (!GeometryUtility.TestPlanesAABB(planes, sampleBounds))
                continue;

            if (IsSampleVisible(samplePoints[i], boundaryObject))
                return false;
        }

        return true;
    }

    private bool IsSampleVisible(Vector3 samplePoint, GameObject boundaryObject)
    {
        Vector3 origin = playerCamera.transform.position;
        Vector3 direction = samplePoint - origin;
        float distance = direction.magnitude;
        if (distance <= 0.001f)
            return true;

        RaycastHit[] hits = Physics.RaycastAll(origin, direction / distance, distance + 0.05f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, CompareHitsByDistance);

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null)
                continue;

            if (playerTransform != null && hitCollider.transform.IsChildOf(playerTransform))
                continue;

            if (boundaryObject != null && hitCollider.transform.IsChildOf(boundaryObject.transform))
                return true;

            if (hits[i].distance < distance - 0.02f)
                return false;
        }

        return boundaryObject == null;
    }

    private static int CompareHitsByDistance(RaycastHit left, RaycastHit right)
    {
        return left.distance.CompareTo(right.distance);
    }

    private Bounds GetBoundaryBounds(BoundaryKey boundary)
    {
        if (boundaryObjects.TryGetValue(boundary, out GameObject boundaryObject) && boundaryObject != null)
        {
            Renderer[] renderers = boundaryObject.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);

                return bounds;
            }
        }

        Vector3 size = boundary.Side == Direction.North || boundary.Side == Direction.South
            ? new Vector3(CellSize, FloorHeight, BoundaryDepth)
            : new Vector3(BoundaryDepth, FloorHeight, CellSize);

        return new Bounds(GetBoundaryWorldPosition(boundary.Cell, boundary.Side) + Vector3.up * (FloorHeight * 0.5f), size);
    }

    private Vector3[] GetBoundarySamplePoints(BoundaryKey boundary)
    {
        Vector3 center = GetBoundaryWorldPosition(boundary.Cell, boundary.Side) + Vector3.up * VisibilityVerticalOffset;
        Vector3 lateral = GetDirectionVector(TurnRight(boundary.Side)) * VisibilityLateralOffset;
        return new[]
        {
            center,
            center + lateral,
            center - lateral,
            center + Vector3.up * 0.6f,
            center - Vector3.up * 0.45f
        };
    }

    private GameObject GetBoundaryPrefab(BoundaryVisualType type)
    {
        switch (type)
        {
            case BoundaryVisualType.Wall:
                return wallPrefab;
            case BoundaryVisualType.WindowWall:
                return windowWallPrefab;
            case BoundaryVisualType.DoorWall:
                return doorWallPrefab;
            default:
                return null;
        }
    }

    private Vector3 ComputeInitialOrigin(Vector3 playerPosition, Direction direction)
    {
        Vector3 start = playerPosition + GetDirectionVector(direction) * InitialDoorDistance;
        float snappedX = Mathf.Round(start.x / CellSize) * CellSize;
        float snappedY = Mathf.Round(playerPosition.y / FloorHeight) * FloorHeight;
        float snappedZ = Mathf.Round(start.z / CellSize) * CellSize;
        return new Vector3(snappedX, snappedY, snappedZ);
    }

    private Bounds GetCellBounds(CellSpec spec)
    {
        Vector3 center = GetCellWorldPosition(spec.Coord);
        if (spec.Kind == CellKind.StairBase)
            return new Bounds(center + new Vector3(0f, FloorHeight * 0.5f, 0f), new Vector3(CellSize, FloorHeight * 2f, CellSize));

        return new Bounds(center + new Vector3(0f, FloorHeight * 0.5f, 0f), new Vector3(CellSize, FloorHeight, CellSize));
    }

    private GridCoord OffsetFromEntrance(GridCoord entranceCell, Direction forward, int forwardSteps, int lateralSteps)
    {
        GridCoord coord = entranceCell;
        Direction lateralDirection = lateralSteps >= 0 ? TurnRight(forward) : TurnLeft(forward);
        for (int i = 0; i < forwardSteps; i++)
            coord = coord.Step(forward);

        for (int i = 0; i < Mathf.Abs(lateralSteps); i++)
            coord = coord.Step(lateralDirection);

        return coord;
    }

    private Frontier CreateRoomExit(
        GridCoord entranceCell,
        Direction entryDirection,
        int depth,
        int leftCells,
        int rightCells,
        Direction exitDirection)
    {
        if (exitDirection == entryDirection)
        {
            int lateral = random.Next(-leftCells, rightCells + 1);
            GridCoord exitCell = OffsetFromEntrance(entranceCell, entryDirection, depth - 1, lateral);
            return new Frontier(exitCell, exitDirection);
        }

        if (exitDirection == TurnLeft(entryDirection))
        {
            int forward = depth > 1 ? random.Next(1, depth) : 0;
            GridCoord exitCell = OffsetFromEntrance(entranceCell, entryDirection, forward, -leftCells);
            return new Frontier(exitCell, exitDirection);
        }

        int selectedForward = depth > 1 ? random.Next(1, depth) : 0;
        GridCoord selectedExitCell = OffsetFromEntrance(entranceCell, entryDirection, selectedForward, rightCells);
        return new Frontier(selectedExitCell, exitDirection);
    }

    private Direction[] BuildRoomExitChoices(Direction entryDirection)
    {
        return new[]
        {
            entryDirection,
            TurnLeft(entryDirection),
            TurnRight(entryDirection)
        };
    }

    private Vector3 GetCellWorldPosition(GridCoord coord)
    {
        return worldGridOrigin + new Vector3(coord.X * CellSize, coord.Level * FloorHeight, coord.Z * CellSize);
    }

    private Vector3 GetBoundaryWorldPosition(GridCoord cell, Direction side)
    {
        return GetCellWorldPosition(cell) + GetDirectionVector(side) * HalfCellSize;
    }

    private static Direction GetClosestDirection(Vector3 worldForward)
    {
        Vector3 flattened = new Vector3(worldForward.x, 0f, worldForward.z);
        if (flattened.sqrMagnitude <= 0.0001f)
            return Direction.North;

        flattened.Normalize();
        float bestDot = float.NegativeInfinity;
        Direction bestDirection = Direction.North;
        for (int i = 0; i < AllDirections.Length; i++)
        {
            float dot = Vector3.Dot(flattened, GetDirectionVector(AllDirections[i]));
            if (dot <= bestDot)
                continue;

            bestDot = dot;
            bestDirection = AllDirections[i];
        }

        return bestDirection;
    }

    private static BoundaryKey GetCanonicalBoundary(GridCoord cell, Direction side)
    {
        GridCoord neighbor = cell.Step(side);
        if (neighbor.CompareTo(cell) < 0)
            return new BoundaryKey(neighbor, Opposite(side));

        return new BoundaryKey(cell, side);
    }

    private static Vector3 GetDirectionVector(Direction direction)
    {
        switch (direction)
        {
            case Direction.North:
                return Vector3.forward;
            case Direction.East:
                return Vector3.right;
            case Direction.South:
                return Vector3.back;
            default:
                return Vector3.left;
        }
    }

    private static float GetDirectionYaw(Direction direction)
    {
        switch (direction)
        {
            case Direction.North:
                return 0f;
            case Direction.East:
                return 90f;
            case Direction.South:
                return 180f;
            default:
                return 270f;
        }
    }

    private static Direction TurnLeft(Direction direction)
    {
        return (Direction)(((int)direction + 3) % 4);
    }

    private static Direction TurnRight(Direction direction)
    {
        return (Direction)(((int)direction + 1) % 4);
    }

    private static Direction Opposite(Direction direction)
    {
        return (Direction)(((int)direction + 2) % 4);
    }

    private void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int swapIndex = random.Next(i + 1);
            T temp = list[i];
            list[i] = list[swapIndex];
            list[swapIndex] = temp;
        }
    }

    private void Shuffle(Direction[] list)
    {
        for (int i = list.Length - 1; i > 0; i--)
        {
            int swapIndex = random.Next(i + 1);
            Direction temp = list[i];
            list[i] = list[swapIndex];
            list[swapIndex] = temp;
        }
    }

    private enum Direction
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3
    }

    private enum SegmentType
    {
        Room,
        Hallway,
        Staircase
    }

    private enum CellKind
    {
        Standard,
        StairBase,
        StairLanding
    }

    private enum BoundaryVisualType
    {
        None,
        Wall,
        WindowWall,
        DoorWall
    }

    private readonly struct Frontier
    {
        public Frontier(GridCoord sourceCell, Direction direction)
        {
            SourceCell = sourceCell;
            Direction = direction;
        }

        public GridCoord SourceCell { get; }

        public Direction Direction { get; }
    }

    private readonly struct BoundaryKey : IEquatable<BoundaryKey>
    {
        public BoundaryKey(GridCoord cell, Direction side)
        {
            Cell = cell;
            Side = side;
        }

        public GridCoord Cell { get; }

        public Direction Side { get; }

        public bool Equals(BoundaryKey other)
        {
            return Cell.Equals(other.Cell) && Side == other.Side;
        }

        public override bool Equals(object obj)
        {
            return obj is BoundaryKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Cell.GetHashCode() * 397) ^ (int)Side;
        }
    }

    private readonly struct GridCoord : IEquatable<GridCoord>, IComparable<GridCoord>
    {
        public static readonly GridCoord Zero = new GridCoord(0, 0, 0);

        public GridCoord(int x, int level, int z)
        {
            X = x;
            Level = level;
            Z = z;
        }

        public int X { get; }

        public int Level { get; }

        public int Z { get; }

        public GridCoord Step(Direction direction)
        {
            switch (direction)
            {
                case Direction.North:
                    return new GridCoord(X, Level, Z + 1);
                case Direction.East:
                    return new GridCoord(X + 1, Level, Z);
                case Direction.South:
                    return new GridCoord(X, Level, Z - 1);
                default:
                    return new GridCoord(X - 1, Level, Z);
            }
        }

        public GridCoord WithLevel(int newLevel)
        {
            return new GridCoord(X, newLevel, Z);
        }

        public int CompareTo(GridCoord other)
        {
            if (Level != other.Level)
                return Level.CompareTo(other.Level);

            if (Z != other.Z)
                return Z.CompareTo(other.Z);

            return X.CompareTo(other.X);
        }

        public bool Equals(GridCoord other)
        {
            return X == other.X && Level == other.Level && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is GridCoord other && Equals(other);
        }

        public override int GetHashCode()
        {
            int hash = X;
            hash = (hash * 397) ^ Level;
            hash = (hash * 397) ^ Z;
            return hash;
        }
    }

    private readonly struct CellSpec
    {
        public CellSpec(GridCoord coord, CellKind kind, Direction primaryDirection)
        {
            Coord = coord;
            Kind = kind;
            PrimaryDirection = primaryDirection;
        }

        public GridCoord Coord { get; }

        public CellKind Kind { get; }

        public Direction PrimaryDirection { get; }
    }

    private sealed class SegmentBlueprint
    {
        private readonly HashSet<GridCoord> cellLookup = new HashSet<GridCoord>();
        private readonly List<CellSpec> cells = new List<CellSpec>();
        private readonly List<BoundaryKey> openBoundaries = new List<BoundaryKey>();

        public SegmentType Type { get; set; }

        public Frontier ExitFrontier { get; set; }

        public GridCoord EntranceDoorCell { get; set; }

        public Direction EntranceDoorSide { get; set; }

        public bool UsesOpenEntrance { get; set; }

        public IReadOnlyList<CellSpec> Cells => cells;

        public int CellCount => cells.Count;

        public IReadOnlyList<BoundaryKey> OpenBoundaries => openBoundaries;

        public int OpenBoundaryCount => openBoundaries.Count;

        public void AddCell(CellSpec spec)
        {
            if (cellLookup.Add(spec.Coord))
                cells.Add(spec);
        }

        public bool Contains(GridCoord coord)
        {
            return cellLookup.Contains(coord);
        }

        public void AddOpenBoundary(BoundaryKey boundary)
        {
            if (!openBoundaries.Contains(boundary))
                openBoundaries.Add(boundary);
        }
    }

    private sealed class SegmentData
    {
        public int Index;
        public SegmentType Type;
        public Frontier ExitFrontier;
        public Bounds WorldBounds;
        public Transform Root;
        public readonly List<GridCoord> Cells = new List<GridCoord>();
    }

    private sealed class CellData
    {
        public GridCoord Coord;
        public CellKind Kind;
        public Direction PrimaryDirection;
        public GameObject Root;
        public int SegmentIndex;

        public bool SuppressesBoundary(Direction side)
        {
            if (Kind == CellKind.StairBase)
            {
                return side == PrimaryDirection ||
                       side == ProceduralHouseGenerator.Opposite(PrimaryDirection);
            }

            return Kind == CellKind.StairLanding && side == ProceduralHouseGenerator.Opposite(PrimaryDirection);
        }
    }
}
