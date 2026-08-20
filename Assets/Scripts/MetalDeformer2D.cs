using System.Collections.Generic;
using UnityEngine;

namespace ForgingPrototype
{
    /// <summary>
    /// Feel v3: spatial brush + local surface tension.
    /// Moves metal where you hit, then relaxes only the struck region so edges stay soft like hot metal
    /// (without ring-spreading strike force that dragged distant parts).
    /// </summary>
    public class MetalDeformer2D : MonoBehaviour
    {
        public enum StartingShape
        {
            Oval,
            Rectangle
        }

        public enum InwardHandling
        {
            Crease = 0,
            LateralFlow = 1,
            Blend = 2
        }

        [Header("Starting Shape")]
        [SerializeField] StartingShape startingShape = StartingShape.Oval;
        [SerializeField] int vertexCount = 24;
        [SerializeField] Vector2 ovalRadii = new Vector2(1.4f, 0.7f);
        [SerializeField] Vector2 rectangleSize = new Vector2(2.4f, 1.1f);
        [SerializeField] Vector2 shapeCenter = Vector2.zero;

        [Header("Spatial Brush")]
        [SerializeField] float impactRadius = 0.74f;
        [SerializeField] float falloffExponent = 1.8f;
        [SerializeField] float minStrikeStrength = 0.16f;
        [SerializeField] float maxStrikeStrength = 0.7f;
        [SerializeField, Range(0f, 0.25f)] float minInfluence = 0.02f;
        [SerializeField, Range(0f, 1f)] float inflateMix = 0.12f;

        [Header("Edge Split")]
        [SerializeField] bool splitEdgeUnderHammer = true;
        [SerializeField] float splitIfFartherThan = 0.08f;
        [SerializeField] int maxVertices = 48;

        [Header("Local Surface Tension (hot-metal feel)")]
        [Tooltip("Laplacian relax only near the hit. Smooths jagged spikes without dragging far edges by strike force.")]
        [SerializeField, Range(0f, 1f)] float surfaceTension = 0.55f;
        [SerializeField] int tensionIterations = 4;
        [Tooltip("Also relax immediate neighbors of struck verts (smooth only, not strike push).")]
        [SerializeField] bool tensionIncludesAdjacency = true;
        [SerializeField] float sharpAngleDegrees = 55f;
        [SerializeField, Range(0f, 1f)] float sharpCornerExtraSmooth = 0.65f;

        [Header("Local Outline Magnet")]
        [Tooltip("Outline alignment wins over smoothing. Magnet runs last; near-outline verts are protected from tension.")]
        [SerializeField] bool outlineMagnetEnabled = true;
        [SerializeField] float magnetRadius = 0.5f;
        [SerializeField, Range(0f, 1f)] float magnetStrength = 0.365f;
        [SerializeField] float magnetFalloff = 1.4f;
        [Tooltip("Magnet strength fades smoothly with distance from the hammer (as a multiple of impact radius).")]
        [SerializeField] float magnetHitFalloffMultiplier = 2f;
        [Tooltip("Within this distance of the outline, surface tension is reduced/disabled so verts aren't pulled inward.")]
        [SerializeField] float outlineProtectDistance = 0.28f;

        [Header("Split Smoothing")]
        [Tooltip("Extra surface-tension strength when a strike inserts new geometry.")]
        [SerializeField, Range(0f, 1f)] float splitTensionBoost = 0.35f;
        [SerializeField] int splitExtraTensionIterations = 3;

        [Header("Inward Handling")]
        [Tooltip("Crease = soft dent. LateralFlow = slide along boundary. Blend = mix of both.")]
        [SerializeField] InwardHandling inwardHandling = InwardHandling.Blend;
        [Tooltip("If strike aims inward past this dot threshold vs local outward, use inward handling.")]
        [SerializeField, Range(0f, 1f)] float inwardDotThreshold = 0.2f;
        [Tooltip("For Blend mode: 0 = pure lateral flow, 1 = pure crease dent.")]
        [SerializeField, Range(0f, 1f)] float inwardCreaseBlend = 0.45f;
        [SerializeField] float creaseRadiusMultiplier = 1.35f;
        [SerializeField, Range(0.25f, 1.5f)] float creaseStrengthScale = 0.75f;
        [SerializeField, Range(0f, 1f)] float creaseTensionBoost = 0.45f;
        [SerializeField] int creaseExtraTensionIterations = 3;
        [Tooltip("Inflate mix used for lateral / blend lateral portion (usually 0).")]
        [SerializeField, Range(0f, 1f)] float lateralFlowInflateMix = 0f;
        [SerializeField] bool disableSplitOnInward = true;
        [SerializeField] bool disableMagnetOnInward = false;

        [Header("Stability")]
        [SerializeField] float maxVertexTravelPerStrike = 0.95f;
        [SerializeField] float minEdgeLength = 0.06f;

        [Header("Metal Type + Heat")]
        [SerializeField] MetalType metalType;
        [SerializeField, Range(0f, 1f)] float heat = 0.55f;

        readonly List<Vector2> _vertices = new List<Vector2>();
        readonly List<Vector2> _initialVertices = new List<Vector2>();
        readonly List<Vector2> _smoothBuffer = new List<Vector2>();
        readonly List<float> _influenceBuffer = new List<float>();
        readonly List<float> _tensionMask = new List<float>();
        readonly List<bool> _movedThisStrike = new List<bool>();
        Vector2[] _targetOutline;
        bool _splitThisStrike;
        bool _inwardSpecialThisStrike;
        bool _inwardCreaseThisStrike;
        float _strikeMagnetMultiplier = 1f;
        float _strikeTensionMultiplier = 1f;

        public IReadOnlyList<Vector2> Vertices => _vertices;
        public int VertexCount => _vertices.Count;
        public float ImpactRadius => impactRadius;
        public float MinStrikeStrength => minStrikeStrength;
        public float MaxStrikeStrength => maxStrikeStrength;
        public float FalloffExponent => falloffExponent;
        public float Heat
        {
            get => heat;
            set => heat = Mathf.Clamp01(value);
        }
        public Vector2 ShapeCenter
        {
            get => shapeCenter;
            set => shapeCenter = value;
        }
        public MetalType MetalType => metalType;
        public string MetalDisplayName => metalType != null ? metalType.displayName : "Metal";
        public MetalFeel CurrentFeel => SampleFeel();

        public event System.Action VerticesChanged;
        public event System.Action<Vector2, float> Struck;
        public event System.Action MetalTypeChanged;

        void Awake()
        {
            if (_vertices.Count == 0)
            {
                InitializeShape();
            }
        }

        public void SetTargetOutline(Vector2[] targetVertices)
        {
            _targetOutline = targetVertices;
        }

        public void SetMetalType(MetalType type, float startingHeat = -1f)
        {
            metalType = type;
            if (startingHeat >= 0f)
            {
                heat = Mathf.Clamp01(startingHeat);
            }

            MetalTypeChanged?.Invoke();
        }

        public void TickHeat(float deltaTime, bool holdHeatPressed)
        {
            if (metalType == null || deltaTime <= 0f)
            {
                return;
            }

            if (holdHeatPressed)
            {
                heat = Mathf.Clamp01(heat + metalType.holdHeatRate * deltaTime);
            }
            else
            {
                heat = Mathf.Clamp01(heat - metalType.coolRate * deltaTime);
            }
        }

        public MetalFeel SampleFeel()
        {
            return metalType.Sample(heat);
        }

        public void InitializeShape()
        {
            _vertices.Clear();
            _initialVertices.Clear();
            vertexCount = Mathf.Clamp(vertexCount, 8, maxVertices);

            if (startingShape == StartingShape.Oval)
            {
                for (int i = 0; i < vertexCount; i++)
                {
                    float t = (i / (float)vertexCount) * Mathf.PI * 2f;
                    _vertices.Add(shapeCenter + new Vector2(Mathf.Cos(t) * ovalRadii.x, Mathf.Sin(t) * ovalRadii.y));
                }
            }
            else
            {
                float w = rectangleSize.x;
                float h = rectangleSize.y;
                float perimeter = 2f * (w + h);
                for (int i = 0; i < vertexCount; i++)
                {
                    float d = (i / (float)vertexCount) * perimeter;
                    Vector2 local;
                    if (d < w)
                    {
                        local = new Vector2(-w * 0.5f + d, -h * 0.5f);
                    }
                    else if (d < w + h)
                    {
                        local = new Vector2(w * 0.5f, -h * 0.5f + (d - w));
                    }
                    else if (d < w + h + w)
                    {
                        local = new Vector2(w * 0.5f - (d - w - h), h * 0.5f);
                    }
                    else
                    {
                        local = new Vector2(-w * 0.5f, h * 0.5f - (d - w - h - w));
                    }

                    _vertices.Add(shapeCenter + local);
                }
            }

            _initialVertices.AddRange(_vertices);
            VerticesChanged?.Invoke();
        }

        public void LoadVertices(IReadOnlyList<Vector2> source, bool replaceInitialSnapshot = false)
        {
            if (source == null || source.Count < 3)
            {
                return;
            }

            _vertices.Clear();
            for (int i = 0; i < source.Count; i++)
            {
                _vertices.Add(source[i]);
            }

            if (replaceInitialSnapshot || _initialVertices.Count == 0)
            {
                _initialVertices.Clear();
                _initialVertices.AddRange(_vertices);
            }

            VerticesChanged?.Invoke();
        }

        public void CopyVerticesTo(List<Vector2> destination)
        {
            if (destination == null)
            {
                return;
            }

            destination.Clear();
            destination.AddRange(_vertices);
        }

        public void ResetShape()
        {
            _vertices.Clear();
            _vertices.AddRange(_initialVertices);
            VerticesChanged?.Invoke();
        }

        public Vector2 GetCentroid() => ComputeCentroid();

        public Vector2 GetResolvedStrikeDirection(Vector2 impactPoint, Vector2 requestedDirection, bool explicitAim)
        {
            if (explicitAim && requestedDirection.sqrMagnitude >= 0.0001f)
                return requestedDirection.normalized;

            if (_vertices.Count < 3)
            {
                if (requestedDirection.sqrMagnitude >= 0.0001f)
                    return requestedDirection.normalized;
                return Vector2.up;
            }

            Vector2 centroid = ComputeCentroid();
            Vector2 fromCenter = impactPoint - centroid;
            if (fromCenter.sqrMagnitude >= 0.0001f)
                return fromCenter.normalized;

            return EstimateOutwardAt(impactPoint, centroid);
        }

        public bool TryStrike(Vector2 impactPoint, Vector2 direction, float charge01)
        {
            return TryStrike(impactPoint, direction, charge01, false);
        }

        public bool TryStrike(Vector2 impactPoint, Vector2 direction, float charge01, bool explicitAim)
        {
            if (_vertices.Count < 3)
                return false;

            direction = GetResolvedStrikeDirection(impactPoint, direction, explicitAim);
            charge01 = Mathf.Clamp01(charge01);

            MetalFeel feel = SampleFeel();
            _strikeMagnetMultiplier = feel.magnet;
            _strikeTensionMultiplier = feel.tension;

            float strength = Mathf.Lerp(minStrikeStrength, maxStrikeStrength, charge01) * feel.mobility;
            if (strength <= 0.0001f)
                return false;

            _splitThisStrike = false;
            _inwardSpecialThisStrike = false;
            _inwardCreaseThisStrike = false;

            Vector2 centroid = ComputeCentroid();
            Vector2 localOutward = EstimateOutwardAt(impactPoint, centroid);
            float outwardDot = Vector2.Dot(direction, localOutward);
            bool inwardStrike = !explicitAim && outwardDot <= -inwardDotThreshold;

            bool allowSplit = splitEdgeUnderHammer && !(inwardStrike && disableSplitOnInward);
            int focusIndex = EnsureVertexNearImpact(impactPoint, allowSplit);

            if (inwardStrike)
            {
                _inwardSpecialThisStrike = true;
                if (inwardHandling == InwardHandling.LateralFlow)
                {
                    Vector2 lateral = RemapInwardToLateral(direction, localOutward);
                    ApplyOutwardBrush(impactPoint, lateral, focusIndex, centroid, strength, lateralFlowInflateMix);
                }
                else if (inwardHandling == InwardHandling.Crease)
                {
                    ApplyInwardCrease(impactPoint, localOutward, focusIndex, strength);
                }
                else
                {
                    ApplyInwardBlend(impactPoint, direction, localOutward, focusIndex, centroid, strength);
                }
            }
            else
            {
                ApplyOutwardBrush(impactPoint, direction, focusIndex, centroid, strength, inflateMix);
            }

            BuildTensionMask(impactPoint);

            float tensionScale = _strikeTensionMultiplier;
            int tensionIters = tensionIterations;
            if (_splitThisStrike)
            {
                tensionScale += splitTensionBoost;
                tensionIters += splitExtraTensionIterations;
            }

            if (_inwardCreaseThisStrike)
            {
                float creaseWeight = inwardHandling == InwardHandling.Blend
                    ? inwardCreaseBlend
                    : 1f;
                tensionScale += creaseTensionBoost * creaseWeight;
                tensionIters += Mathf.RoundToInt(creaseExtraTensionIterations * creaseWeight);
            }

            ApplySurfaceTension(tensionScale, tensionIters);
            CollapseTinyEdgesNear(impactPoint);

            if (!(_inwardSpecialThisStrike && disableMagnetOnInward))
            {
                ApplyLocalOutlineMagnet(impactPoint);
            }

            VerticesChanged?.Invoke();
            Struck?.Invoke(impactPoint, impactRadius);
            return true;
        }

        static Vector2 RemapInwardToLateral(Vector2 direction, Vector2 outward)
        {
            // Strip the inward/outward component so metal slides along the boundary instead of punching in.
            Vector2 lateral = direction - outward * Vector2.Dot(direction, outward);
            if (lateral.sqrMagnitude >= 0.04f)
            {
                return lateral.normalized;
            }

            Vector2 tangent = new Vector2(-outward.y, outward.x);
            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = Vector2.right;
            }
            else
            {
                tangent.Normalize();
            }

            // Pure inward: pick the tangent side that best matches any remaining stick bias / world right.
            float prefer = Vector2.Dot(direction, tangent);
            if (Mathf.Abs(prefer) < 0.01f)
            {
                prefer = Vector2.Dot(Vector2.right, tangent);
            }

            if (Mathf.Abs(prefer) < 0.01f)
            {
                prefer = 1f;
            }

            return tangent * Mathf.Sign(prefer);
        }

        void ApplyOutwardBrush(
            Vector2 impactPoint,
            Vector2 direction,
            int focusIndex,
            Vector2 centroid,
            float strength,
            float inflateAmount)
        {
            _influenceBuffer.Clear();
            _movedThisStrike.Clear();
            bool anyMoved = false;

            for (int i = 0; i < _vertices.Count; i++)
            {
                float dist = Vector2.Distance(_vertices[i], impactPoint);
                float influence = 0f;
                if (dist <= impactRadius)
                {
                    float t = 1f - dist / impactRadius;
                    float smooth = t * t * (3f - 2f * t);
                    influence = Mathf.Pow(smooth, falloffExponent);
                    if (influence < minInfluence)
                    {
                        influence = 0f;
                    }
                }

                if (i == focusIndex)
                {
                    influence = Mathf.Max(influence, 0.65f);
                }

                _influenceBuffer.Add(influence);
                if (influence <= 0f)
                {
                    _movedThisStrike.Add(false);
                    continue;
                }

                Vector2 outward = _vertices[i] - centroid;
                if (outward.sqrMagnitude < 0.0001f)
                {
                    outward = direction;
                }
                else
                {
                    outward.Normalize();
                }

                Vector2 pushDir = Vector2.Lerp(direction, outward, inflateAmount).normalized;
                float move = Mathf.Min(strength * influence, maxVertexTravelPerStrike);
                _vertices[i] += pushDir * move;
                _movedThisStrike.Add(true);
                anyMoved = true;
            }

            if (!anyMoved)
            {
                _vertices[focusIndex] += direction * strength;
                if (focusIndex < _influenceBuffer.Count)
                {
                    _influenceBuffer[focusIndex] = 1f;
                }

                while (_movedThisStrike.Count < _vertices.Count)
                {
                    _movedThisStrike.Add(false);
                }

                _movedThisStrike[focusIndex] = true;
            }
        }

        void ApplyInwardCrease(Vector2 impactPoint, Vector2 localOutward, int focusIndex, float strength)
        {
            _inwardCreaseThisStrike = true;
            Vector2 inward = -localOutward;
            Vector2 centroid = ComputeCentroid();
            float creaseRadius = impactRadius * Mathf.Max(1f, creaseRadiusMultiplier);
            float creaseStrength = strength * creaseStrengthScale;

            _influenceBuffer.Clear();
            _movedThisStrike.Clear();
            bool anyMoved = false;

            for (int i = 0; i < _vertices.Count; i++)
            {
                float dist = Vector2.Distance(_vertices[i], impactPoint);
                float influence = 0f;
                if (dist <= creaseRadius)
                {
                    float t = 1f - dist / creaseRadius;
                    // Wider, softer lobe than the outward brush — dent instead of stab.
                    float smooth = t * t * (3f - 2f * t);
                    influence = smooth * smooth;
                    if (influence < minInfluence * 0.5f)
                    {
                        influence = 0f;
                    }
                }

                if (i == focusIndex)
                {
                    influence = Mathf.Max(influence, 0.55f);
                }

                _influenceBuffer.Add(influence);
                if (influence <= 0f)
                {
                    _movedThisStrike.Add(false);
                    continue;
                }

                // Shared inward from the hit plus a little local surface inward — dent, not stab.
                Vector2 vertOutward = _vertices[i] - centroid;
                Vector2 vertInward = vertOutward.sqrMagnitude > 0.0001f
                    ? -vertOutward.normalized
                    : inward;
                Vector2 creaseDir = Vector2.Lerp(inward, vertInward, 0.35f).normalized;

                float move = Mathf.Min(creaseStrength * influence, maxVertexTravelPerStrike * 0.85f);
                _vertices[i] += creaseDir * move;
                _movedThisStrike.Add(true);
                anyMoved = true;
            }

            if (!anyMoved)
            {
                _vertices[focusIndex] += inward * creaseStrength;
                if (focusIndex < _influenceBuffer.Count)
                {
                    _influenceBuffer[focusIndex] = 1f;
                }

                while (_movedThisStrike.Count < _vertices.Count)
                {
                    _movedThisStrike.Add(false);
                }

                _movedThisStrike[focusIndex] = true;
            }
        }

        void ApplyInwardBlend(
            Vector2 impactPoint,
            Vector2 originalDirection,
            Vector2 localOutward,
            int focusIndex,
            Vector2 centroid,
            float strength)
        {
            float creaseWeight = Mathf.Clamp01(inwardCreaseBlend);
            float lateralWeight = 1f - creaseWeight;

            // Any crease contribution gets the softer dent kernel + extra tension.
            if (creaseWeight > 0.001f)
            {
                _inwardCreaseThisStrike = true;
            }

            Vector2 inward = -localOutward;
            Vector2 lateral = RemapInwardToLateral(originalDirection, localOutward);
            float creaseRadius = impactRadius * Mathf.Max(1f, creaseRadiusMultiplier);
            // Use the wider radius whenever crease is in the mix; otherwise normal brush radius.
            float brushRadius = creaseWeight > 0.001f ? Mathf.Lerp(impactRadius, creaseRadius, creaseWeight) : impactRadius;

            _influenceBuffer.Clear();
            _movedThisStrike.Clear();
            bool anyMoved = false;

            for (int i = 0; i < _vertices.Count; i++)
            {
                float dist = Vector2.Distance(_vertices[i], impactPoint);
                float influence = 0f;
                if (dist <= brushRadius)
                {
                    float t = 1f - dist / brushRadius;
                    float smooth = t * t * (3f - 2f * t);
                    // Bias toward crease-style falloff as crease weight rises.
                    influence = Mathf.Lerp(
                        Mathf.Pow(smooth, falloffExponent),
                        smooth * smooth,
                        creaseWeight);
                    if (influence < minInfluence * Mathf.Lerp(1f, 0.5f, creaseWeight))
                        influence = 0f;
                }

                if (i == focusIndex)
                    influence = Mathf.Max(influence, Mathf.Lerp(0.65f, 0.55f, creaseWeight));

                _influenceBuffer.Add(influence);
                if (influence <= 0f)
                {
                    _movedThisStrike.Add(false);
                    continue;
                }

                Vector2 vertOutward = _vertices[i] - centroid;
                Vector2 vertInward = vertOutward.sqrMagnitude > 0.0001f
                    ? -vertOutward.normalized
                    : inward;
                Vector2 creaseDir = Vector2.Lerp(inward, vertInward, 0.35f).normalized;

                Vector2 lateralDir = lateral;
                if (lateralFlowInflateMix > 0f)
                {
                    Vector2 outward = vertOutward.sqrMagnitude > 0.0001f ? vertOutward.normalized : lateral;
                    lateralDir = Vector2.Lerp(lateral, outward, lateralFlowInflateMix).normalized;
                }

                Vector2 push =
                    lateralDir * (strength * lateralWeight) +
                    creaseDir * (strength * creaseStrengthScale * creaseWeight);

                float move = Mathf.Min(push.magnitude * influence, maxVertexTravelPerStrike);
                if (push.sqrMagnitude > 0.0001f)
                {
                    _vertices[i] += push.normalized * move;
                    _movedThisStrike.Add(true);
                    anyMoved = true;
                }
                else
                    _movedThisStrike.Add(false);
            }

            if (!anyMoved)
            {
                Vector2 fallback =
                    lateral * lateralWeight +
                    inward * (creaseStrengthScale * creaseWeight);
                if (fallback.sqrMagnitude < 0.0001f)
                    fallback = lateral;

                _vertices[focusIndex] += fallback.normalized * strength;
                if (focusIndex < _influenceBuffer.Count)
                    _influenceBuffer[focusIndex] = 1f;

                while (_movedThisStrike.Count < _vertices.Count)
                    _movedThisStrike.Add(false);

                _movedThisStrike[focusIndex] = true;
            }
        }

        Vector2 EstimateOutwardAt(Vector2 point, Vector2 centroid)
        {
            // Prefer boundary normal at the closest edge; fall back to centroid ray.
            int edgeIndex = 0;
            Vector2 closest = _vertices[0];
            float bestDist = float.MaxValue;
            for (int i = 0; i < _vertices.Count; i++)
            {
                Vector2 a = _vertices[i];
                Vector2 b = _vertices[(i + 1) % _vertices.Count];
                Vector2 c = ClosestPointOnSegment(point, a, b);
                float d = Vector2.Distance(point, c);
                if (d < bestDist)
                {
                    bestDist = d;
                    closest = c;
                    edgeIndex = i;
                }
            }

            Vector2 a0 = _vertices[edgeIndex];
            Vector2 b0 = _vertices[(edgeIndex + 1) % _vertices.Count];
            Vector2 edge = b0 - a0;
            if (edge.sqrMagnitude > 0.0001f)
            {
                // CCW boundary → outward is right normal (edge.y, -edge.x).
                // If winding is CW, flip using signed area.
                Vector2 outward = new Vector2(edge.y, -edge.x).normalized;
                if (ComputeSignedArea() < 0f)
                {
                    outward = -outward;
                }

                // Keep outward pointing away from centroid when ambiguous.
                if (Vector2.Dot(outward, closest - centroid) < 0f)
                {
                    outward = -outward;
                }

                return outward;
            }

            Vector2 fromCenter = point - centroid;
            return fromCenter.sqrMagnitude > 0.0001f ? fromCenter.normalized : Vector2.up;
        }

        float ComputeSignedArea()
        {
            float area = 0f;
            for (int i = 0; i < _vertices.Count; i++)
            {
                Vector2 a = _vertices[i];
                Vector2 b = _vertices[(i + 1) % _vertices.Count];
                area += a.x * b.y - b.x * a.y;
            }

            return area * 0.5f;
        }

        int EnsureVertexNearImpact(Vector2 impactPoint, bool allowSplit)
        {
            int edgeIndex = 0;
            Vector2 closestOnEdge = _vertices[0];
            float bestDist = float.MaxValue;

            for (int i = 0; i < _vertices.Count; i++)
            {
                Vector2 a = _vertices[i];
                Vector2 b = _vertices[(i + 1) % _vertices.Count];
                Vector2 c = ClosestPointOnSegment(impactPoint, a, b);
                float d = Vector2.Distance(impactPoint, c);
                if (d < bestDist)
                {
                    bestDist = d;
                    closestOnEdge = c;
                    edgeIndex = i;
                }
            }

            int indexA = edgeIndex;
            int indexB = (edgeIndex + 1) % _vertices.Count;
            float distA = Vector2.Distance(closestOnEdge, _vertices[indexA]);
            float distB = Vector2.Distance(closestOnEdge, _vertices[indexB]);

            if (distA <= splitIfFartherThan && distA <= distB)
            {
                return indexA;
            }

            if (distB <= splitIfFartherThan)
            {
                return indexB;
            }

            if (!allowSplit || _vertices.Count >= maxVertices)
            {
                return distA <= distB ? indexA : indexB;
            }

            int insertAt = indexA + 1;
            _vertices.Insert(insertAt, closestOnEdge);
            _splitThisStrike = true;
            return insertAt;
        }

        void BuildTensionMask(Vector2 impactPoint)
        {
            _tensionMask.Clear();
            int count = _vertices.Count;
            for (int i = 0; i < count; i++)
            {
                float influence = i < _influenceBuffer.Count ? _influenceBuffer[i] : 0f;
                float dist = Vector2.Distance(_vertices[i], impactPoint);
                float spatial = dist <= impactRadius * 1.15f
                    ? 1f - dist / (impactRadius * 1.15f)
                    : 0f;
                _tensionMask.Add(Mathf.Max(influence, spatial));
            }

            if (!tensionIncludesAdjacency)
            {
                return;
            }

            // Expand mask by one ring for smoothing only (does not add strike displacement).
            var expanded = new float[count];
            for (int i = 0; i < count; i++)
            {
                float self = _tensionMask[i];
                float prev = _tensionMask[(i - 1 + count) % count];
                float next = _tensionMask[(i + 1) % count];
                expanded[i] = Mathf.Max(self, Mathf.Max(prev, next) * 0.7f);
            }

            for (int i = 0; i < count; i++)
            {
                _tensionMask[i] = expanded[i];
            }
        }

        void ApplySurfaceTension(float strengthScale = 1f, int iterationOverride = -1)
        {
            float tension = surfaceTension * Mathf.Max(0f, strengthScale);
            int iterations = iterationOverride >= 0 ? iterationOverride : tensionIterations;
            if (tension <= 0f || iterations <= 0)
            {
                return;
            }

            int count = _vertices.Count;
            for (int iter = 0; iter < iterations; iter++)
            {
                _smoothBuffer.Clear();
                for (int i = 0; i < count; i++)
                {
                    Vector2 curr = _vertices[i];
                    float mask = i < _tensionMask.Count ? _tensionMask[i] : 0f;
                    if (mask < 0.01f)
                    {
                        _smoothBuffer.Add(curr);
                        continue;
                    }

                    Vector2 prev = _vertices[(i - 1 + count) % count];
                    Vector2 next = _vertices[(i + 1) % count];
                    Vector2 averaged = (prev + curr + next) / 3f;

                    float blend = tension * mask;

                    // Extra smooth on very sharp corners inside the struck region.
                    Vector2 toPrev = (prev - curr).normalized;
                    Vector2 toNext = (next - curr).normalized;
                    float ang = Vector2.Angle(toPrev, toNext);
                    if (ang < sharpAngleDegrees)
                    {
                        float sharpness = 1f - (ang / sharpAngleDegrees);
                        blend = Mathf.Max(blend, sharpCornerExtraSmooth * sharpness * mask * strengthScale);
                    }

                    // Extra polish on brand-new split geometry and its neighbors.
                    if (_splitThisStrike && i < _movedThisStrike.Count && _movedThisStrike[i])
                    {
                        blend = Mathf.Max(blend, (surfaceTension + splitTensionBoost) * mask);
                    }

                    // Outline priority: don't smooth verts off an outline they're already near.
                    float outlineDist = DistanceToTargetOutline(curr);
                    if (outlineDist < outlineProtectDistance)
                    {
                        float protect = outlineDist / Mathf.Max(0.0001f, outlineProtectDistance);
                        blend *= protect * protect;
                    }

                    blend = Mathf.Clamp01(blend);
                    if (blend <= 0.001f)
                    {
                        _smoothBuffer.Add(curr);
                        continue;
                    }

                    _smoothBuffer.Add(Vector2.Lerp(curr, averaged, blend));
                }

                _vertices.Clear();
                _vertices.AddRange(_smoothBuffer);
            }
        }

        void ApplyLocalOutlineMagnet(Vector2 impactPoint)
        {
            if (!outlineMagnetEnabled || _targetOutline == null || _targetOutline.Length < 2)
            {
                return;
            }

            float hitFalloffRadius = Mathf.Max(0.01f, impactRadius * Mathf.Max(1f, magnetHitFalloffMultiplier));

            for (int i = 0; i < _vertices.Count; i++)
            {
                // Only verts this strike actually pushed.
                bool moved = i < _movedThisStrike.Count && _movedThisStrike[i];
                if (!moved)
                {
                    continue;
                }

                if (!TryClosestPointOnOutline(_vertices[i], out Vector2 closest, out float outlineDist))
                {
                    continue;
                }

                if (outlineDist > magnetRadius)
                {
                    continue;
                }

                // Soft falloff from the hammer — no hard cliff at the brush edge.
                float hitDist = Vector2.Distance(_vertices[i], impactPoint);
                float hitT = 1f - Mathf.Clamp01(hitDist / hitFalloffRadius);
                float hitWeight = hitT * hitT * (3f - 2f * hitT);
                if (hitWeight <= 0.001f)
                {
                    continue;
                }

                float outlineProximity = 1f - outlineDist / magnetRadius;
                float pull = Mathf.Pow(Mathf.Clamp01(outlineProximity), magnetFalloff) *
                             magnetStrength * hitWeight * _strikeMagnetMultiplier;

                if (outlineDist < outlineProtectDistance)
                {
                    float lockAmount = 1f - outlineDist / Mathf.Max(0.0001f, outlineProtectDistance);
                    pull = Mathf.Max(pull, magnetStrength * hitWeight * _strikeMagnetMultiplier * Mathf.Lerp(1f, 1.35f, lockAmount));
                }

                if (pull <= 0.001f)
                {
                    continue;
                }

                _vertices[i] = Vector2.Lerp(_vertices[i], closest, Mathf.Clamp01(pull));
            }
        }

        float DistanceToTargetOutline(Vector2 point)
        {
            if (_targetOutline == null || _targetOutline.Length < 2)
            {
                return float.MaxValue;
            }

            if (!TryClosestPointOnOutline(point, out _, out float dist))
            {
                return float.MaxValue;
            }

            return dist;
        }

        void CollapseTinyEdgesNear(Vector2 impactPoint)
        {
            if (_vertices.Count <= 8 || minEdgeLength <= 0f)
                return;

            // Merge only tiny edges near the impact so split spam doesn't create sawteeth.
            for (int pass = 0; pass < 3; pass++)
            {
                bool merged = false;
                for (int i = 0; i < _vertices.Count && _vertices.Count > 8; i++)
                {
                    int next = (i + 1) % _vertices.Count;
                    Vector2 a = _vertices[i];
                    Vector2 b = _vertices[next];
                    if (Vector2.Distance(a, impactPoint) > impactRadius * 1.25f && Vector2.Distance(b, impactPoint) > impactRadius * 1.25f)
                        continue;

                    if (Vector2.Distance(a, b) < minEdgeLength)
                    {
                        _vertices[i] = (a + b) * 0.5f;
                        _vertices.RemoveAt(next);
                        if (i < _influenceBuffer.Count && next < _influenceBuffer.Count)
                        {
                            _influenceBuffer[i] = Mathf.Max(_influenceBuffer[i], _influenceBuffer[next]);
                            _influenceBuffer.RemoveAt(next);
                        }

                        if (i < _tensionMask.Count && next < _tensionMask.Count)
                        {
                            _tensionMask[i] = Mathf.Max(_tensionMask[i], _tensionMask[next]);
                            _tensionMask.RemoveAt(next);
                        }

                        if (i < _movedThisStrike.Count && next < _movedThisStrike.Count)
                        {
                            _movedThisStrike[i] = _movedThisStrike[i] || _movedThisStrike[next];
                            _movedThisStrike.RemoveAt(next);
                        }

                        merged = true;
                        break;
                    }
                }

                if (!merged)
                    break;
            }
        }

        public float DistanceToBoundary(Vector2 point)
        {
            if (_vertices.Count < 2)
            {
                return float.MaxValue;
            }

            float best = float.MaxValue;
            for (int i = 0; i < _vertices.Count; i++)
            {
                Vector2 a = _vertices[i];
                Vector2 b = _vertices[(i + 1) % _vertices.Count];
                best = Mathf.Min(best, Vector2.Distance(point, ClosestPointOnSegment(point, a, b)));
            }

            return best;
        }

        public bool ContainsPoint(Vector2 point)
        {
            bool inside = false;
            int j = _vertices.Count - 1;
            for (int i = 0; i < _vertices.Count; i++)
            {
                Vector2 pi = _vertices[i];
                Vector2 pj = _vertices[j];
                bool intersect = ((pi.y > point.y) != (pj.y > point.y)) && (point.x < (pj.x - pi.x) * (point.y - pi.y) / ((pj.y - pi.y) + Mathf.Epsilon) + pi.x);
                if (intersect)
                    inside = !inside;

                j = i;
            }

            return inside;
        }

        public Bounds GetBounds()
        {
            if (_vertices.Count == 0)
            {
                return new Bounds(shapeCenter, Vector3.one);
            }

            Vector2 min = _vertices[0];
            Vector2 max = _vertices[0];
            for (int i = 1; i < _vertices.Count; i++)
            {
                min = Vector2.Min(min, _vertices[i]);
                max = Vector2.Max(max, _vertices[i]);
            }

            var center = (min + max) * 0.5f;
            var size = max - min;
            return new Bounds(center, new Vector3(size.x, size.y, 0.1f));
        }

        Vector2 ComputeCentroid()
        {
            Vector2 c = Vector2.zero;
            for (int i = 0; i < _vertices.Count; i++)
            {
                c += _vertices[i];
            }

            return c / Mathf.Max(1, _vertices.Count);
        }

        bool TryClosestPointOnOutline(Vector2 point, out Vector2 closest, out float distance)
        {
            closest = point;
            distance = float.MaxValue;
            if (_targetOutline == null || _targetOutline.Length < 2)
            {
                return false;
            }

            for (int i = 0; i < _targetOutline.Length; i++)
            {
                Vector2 a = _targetOutline[i];
                Vector2 b = _targetOutline[(i + 1) % _targetOutline.Length];
                Vector2 c = ClosestPointOnSegment(point, a, b);
                float d = Vector2.Distance(point, c);
                if (d < distance)
                {
                    distance = d;
                    closest = c;
                }
            }

            return distance < float.MaxValue;
        }

        static Vector2 ClosestPointOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denom = Vector2.Dot(ab, ab);
            if (denom < 0.000001f)
                return a;

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / denom);
            return a + ab * t;
        }

        void OnDrawGizmos()
        {
            if (_vertices == null || _vertices.Count < 2)
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.9f);
            for (int i = 0; i < _vertices.Count; i++)
            {
                Gizmos.DrawLine(_vertices[i], _vertices[(i + 1) % _vertices.Count]);
            }
        }
    }
}
