using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace PixelChat.Art;

internal static class GltfMotionGuideRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly (string A, string B)[] BoneSegments =
    [
        ("head", "neck"),
        ("neck", "chest"),
        ("chest", "spine"),
        ("spine", "hips"),
        ("chest", "leftClavicle"),
        ("leftClavicle", "leftUpperArm"),
        ("leftUpperArm", "leftLowerArm"),
        ("leftLowerArm", "leftHand"),
        ("chest", "rightClavicle"),
        ("rightClavicle", "rightUpperArm"),
        ("rightUpperArm", "rightLowerArm"),
        ("rightLowerArm", "rightHand"),
        ("hips", "leftUpperLeg"),
        ("leftUpperLeg", "leftLowerLeg"),
        ("leftLowerLeg", "leftFoot"),
        ("leftFoot", "leftToe"),
        ("hips", "rightUpperLeg"),
        ("rightUpperLeg", "rightLowerLeg"),
        ("rightLowerLeg", "rightFoot"),
        ("rightFoot", "rightToe"),
    ];

    public static MotionGuideRenderResult Render(
        string contentRootPath,
        MotionClipDefinition clip,
        LayoutSpec layout,
        AnimationSpec spec)
    {
        var assetPath = Path.Combine(contentRootPath, "Assets", "MotionClips", clip.AssetPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(assetPath))
            throw new InvalidOperationException($"Motion clip asset was not found: {clip.AssetPath}");

        SharpGLTF.Schema2.ModelRoot.Load(assetPath);
        var gltf = GltfBinary.Load(assetPath);
        var samples = Sample(gltf, clip, spec.FrameCount);
        var yaw = spec.GuideCameraYawDegrees ?? FacingToYawDegrees(spec.Facing);
        var metadata = new MotionGuideRenderMetadata(
            MotionClipCatalog.RendererId,
            MotionClipCatalog.SkinnedMannequinRenderStyle,
            clip.ClipId,
            clip.AnimationName,
            clip.SourcePackage,
            clip.SourceUrl,
            clip.License,
            spec.Facing,
            yaw,
            spec.FrameCount,
            clip.AssetPath);

        return new MotionGuideRenderResult(
            RenderGuide(layout, clip, samples, yaw, diagnostic: false),
            RenderGuide(layout, clip, samples, yaw, diagnostic: true),
            metadata,
            samples);
    }

    public static double FacingToYawDegrees(string? facing) =>
        SpriteFacing.ToYawDegrees(facing);

    private static IReadOnlyList<MotionGuideFrameSample> Sample(GltfBinary gltf, MotionClipDefinition clip, int frameCount)
    {
        var animation = gltf.Document.Animations.FirstOrDefault(item => string.Equals(item.Name, clip.AnimationName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"GLB animation '{clip.AnimationName}' was not found.");
        var nodeNameToIndex = gltf.Document.Nodes
            .Select((node, index) => new { node.Name, Index = index })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);
        var mappedNodes = clip.BoneMap
            .Where(item => nodeNameToIndex.ContainsKey(item.Value))
            .ToDictionary(item => item.Key, item => nodeNameToIndex[item.Value], StringComparer.OrdinalIgnoreCase);
        if (!mappedNodes.ContainsKey("hips") && !mappedNodes.ContainsKey("root"))
            throw new InvalidOperationException("Motion clip bone map must include hips or root.");
        var skinnedMesh = LoadSkinnedMesh(gltf, clip);

        var animationSamplers = animation.Samplers
            .Select(sampler => new SampledChannelData(
                ReadAccessor(gltf, sampler.Input),
                ReadAccessor(gltf, sampler.Output),
                sampler.Interpolation ?? "LINEAR"))
            .ToList();
        var duration = animationSamplers
            .SelectMany(sampler => sampler.Input)
            .DefaultIfEmpty(1f)
            .Max();
        duration = MathF.Max(0.001f, duration);

        var parents = ParentMap(gltf.Document.Nodes);
        var basePoses = gltf.Document.Nodes.Select(NodePose.FromNode).ToArray();
        var samples = new List<MotionGuideFrameSample>();
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var time = duration * frameIndex / Math.Max(1, frameCount);
            var poses = basePoses.ToArray();
            foreach (var channel in animation.Channels)
            {
                if (channel.Target?.Node is not int nodeIndex || nodeIndex < 0 || nodeIndex >= poses.Length)
                    continue;
                if (channel.Sampler < 0 || channel.Sampler >= animationSamplers.Count)
                    continue;

                var data = animationSamplers[channel.Sampler];
                var pose = poses[nodeIndex] with { Matrix = null };
                switch (channel.Target.Path)
                {
                    case "translation":
                        pose = pose with { Translation = SampleVec3(data, time) };
                        break;
                    case "rotation":
                        pose = pose with { Rotation = SampleRotation(data, time) };
                        break;
                    case "scale":
                        pose = pose with { Scale = SampleVec3(data, time) };
                        break;
                }
                poses[nodeIndex] = pose;
            }

            var worlds = ResolveWorldMatrices(gltf.Document.Nodes, parents, poses);
            var joints = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
            foreach (var (semantic, nodeIndex) in mappedNodes)
            {
                var point = Vector3.Transform(Vector3.Zero, worlds[nodeIndex]);
                joints[semantic] = point;
            }

            var root = joints.TryGetValue("hips", out var hips)
                ? hips
                : joints.GetValueOrDefault("root", Vector3.Zero);
            foreach (var key in joints.Keys.ToList())
            {
                var point = joints[key];
                joints[key] = new Vector3(point.X - root.X, point.Y, point.Z - root.Z);
            }
            var meshPrimitives = skinnedMesh is null
                ? []
                : SkinMesh(skinnedMesh, worlds, root);

            samples.Add(new MotionGuideFrameSample(
                frameIndex,
                Math.Round(time, 4),
                joints,
                meshPrimitives,
                DetectContacts(joints)));
        }

        return samples;
    }

    private static IReadOnlyList<string> DetectContacts(IReadOnlyDictionary<string, Vector3> joints)
    {
        var leftY = FootHeight(joints, "leftFoot", "leftToe");
        var rightY = FootHeight(joints, "rightFoot", "rightToe");
        if (leftY is null || rightY is null)
            return [];

        var lowest = Math.Min(leftY.Value, rightY.Value);
        var height = BodyHeight(joints);
        var threshold = Math.Max(0.02f, height * 0.025f);
        var contacts = new List<string>();
        if (leftY.Value <= lowest + threshold)
            contacts.Add("left_foot");
        if (rightY.Value <= lowest + threshold)
            contacts.Add("right_foot");
        return contacts;
    }

    private static float? FootHeight(IReadOnlyDictionary<string, Vector3> joints, string footKey, string toeKey)
    {
        var values = new List<float>(2);
        if (joints.TryGetValue(footKey, out var foot))
            values.Add(foot.Y);
        if (joints.TryGetValue(toeKey, out var toe))
            values.Add(toe.Y);
        return values.Count == 0 ? null : values.Min();
    }

    private static float BodyHeight(IReadOnlyDictionary<string, Vector3> joints)
    {
        if (joints.Count == 0)
            return 1f;
        return Math.Max(0.001f, joints.Values.Max(point => point.Y) - joints.Values.Min(point => point.Y));
    }

    private static byte[] RenderGuide(
        LayoutSpec layout,
        MotionClipDefinition clip,
        IReadOnlyList<MotionGuideFrameSample> samples,
        double yawDegrees,
        bool diagnostic)
    {
        var rgba = NewCanvas(layout.CanvasWidth, layout.CanvasHeight, 248, 250, 252, 255);
        var zBuffer = Enumerable.Repeat(double.NegativeInfinity, checked(layout.CanvasWidth * layout.CanvasHeight)).ToArray();
        var projected = samples.Select(sample => Project(sample, yawDegrees)).ToList();
        var allPoints = projected
            .SelectMany(frame => frame.Points.Values.Concat(frame.Meshes.SelectMany(mesh => mesh.Vertices).Select(vertex => vertex.Point)))
            .ToList();
        var minY = allPoints.Count == 0 ? 0d : allPoints.Min(point => point.Y);
        var maxY = allPoints.Count == 0 ? 1d : allPoints.Max(point => point.Y);
        var maxAbsX = allPoints.Count == 0 ? 1d : allPoints.Max(point => Math.Abs(point.X));
        maxAbsX = Math.Max(0.001d, maxAbsX);
        var guideAlpha = diagnostic ? (byte)230 : (byte)115;
        var skeletonAlpha = diagnostic ? (byte)235 : (byte)150;

        foreach (var slot in layout.Slots)
        {
            DrawRect(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.Rect, 178, 186, 196, diagnostic ? (byte)255 : (byte)130, 2);
            DrawRect(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.SafeRect, 116, 142, 162, diagnostic ? (byte)220 : (byte)90, 2);
            DrawLine(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.Rect.X + 8, slot.BaselineY, slot.Rect.X + slot.Rect.Width - 8, slot.BaselineY, 56, 116, 164, diagnostic ? (byte)190 : (byte)105);
            DrawLine(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.Root.X, slot.SafeRect.Y, slot.Root.X, slot.BaselineY, 122, 122, 122, diagnostic ? (byte)165 : (byte)70);
            DrawCircle(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.Root.X, slot.Root.Y, Math.Max(5, slot.Rect.Width / 96), 28, 86, 146, guideAlpha);

            var frame = projected.FirstOrDefault(item => item.Index == slot.FrameIndex);
            if (frame is null)
                continue;

            var verticalRange = Math.Max(0.001d, maxY - minY);
            var scale = Math.Min(
                slot.SafeRect.Width * 0.42d / maxAbsX,
                slot.SafeRect.Height * 0.88d / verticalRange);
            scale = Math.Max(1d, scale);
            var screen = frame.Points.ToDictionary(
                item => item.Key,
                item => new SpriteSheetPoint(
                    slot.Root.X + (int)Math.Round(item.Value.X * scale),
                    slot.BaselineY - (int)Math.Round((item.Value.Y - minY) * scale)),
                StringComparer.OrdinalIgnoreCase);

            DrawMannequin(rgba, zBuffer, layout.CanvasWidth, layout.CanvasHeight, frame.Meshes, slot, scale, minY, diagnostic);
            DrawRect(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.Rect, 178, 186, 196, diagnostic ? (byte)255 : (byte)150, 2);
            DrawRect(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.SafeRect, 116, 142, 162, diagnostic ? (byte)220 : (byte)110, 2);
            DrawLine(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.Rect.X + 8, slot.BaselineY, slot.Rect.X + slot.Rect.Width - 8, slot.BaselineY, 56, 116, 164, diagnostic ? (byte)190 : (byte)120);
            DrawLine(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.Root.X, slot.SafeRect.Y, slot.Root.X, slot.BaselineY, 122, 122, 122, diagnostic ? (byte)165 : (byte)85);
            DrawCircle(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.Root.X, slot.Root.Y, Math.Max(5, slot.Rect.Width / 96), 28, 86, 146, diagnostic ? (byte)230 : (byte)150);

            DrawHandEnvelope(rgba, layout.CanvasWidth, layout.CanvasHeight, screen, slot, diagnostic);

            if (diagnostic)
            {
                foreach (var (a, b) in BoneSegments)
                {
                    if (screen.TryGetValue(a, out var start) && screen.TryGetValue(b, out var end))
                        DrawThickLine(rgba, layout.CanvasWidth, layout.CanvasHeight, start.X, start.Y, end.X, end.Y, 4, 68, 98, 136, skeletonAlpha);
                }

                foreach (var point in screen.Values)
                    DrawCircle(rgba, layout.CanvasWidth, layout.CanvasHeight, point.X, point.Y, 4, 46, 82, 128, skeletonAlpha);

                if (screen.TryGetValue("head", out var head))
                    DrawCircle(rgba, layout.CanvasWidth, layout.CanvasHeight, head.X, head.Y, Math.Max(6, slot.SafeRect.Width / 36), 74, 105, 146, 140);
            }

            DrawContactMarker(rgba, layout.CanvasWidth, layout.CanvasHeight, screen, frame.Contacts, "left_foot", "leftFoot", "leftToe", diagnostic);
            DrawContactMarker(rgba, layout.CanvasWidth, layout.CanvasHeight, screen, frame.Contacts, "right_foot", "rightFoot", "rightToe", diagnostic);

            if (diagnostic)
            {
                var label = $"F{frame.Index + 1} {frame.TimeSeconds:0.00}S {ContactLabel(frame.Contacts)} YAW{yawDegrees:0}";
                DrawText(rgba, layout.CanvasWidth, layout.CanvasHeight, slot.Rect.X + 10, slot.Rect.Y + 10, label.ToUpperInvariant(), 2, 42, 64, 88, 230);
            }
        }

        if (diagnostic)
            DrawText(rgba, layout.CanvasWidth, layout.CanvasHeight, 10, Math.Max(10, layout.CanvasHeight - 24), $"CLIP {clip.ClipId}".ToUpperInvariant(), 2, 42, 64, 88, 230);

        return SpriteSheetPngCodec.EncodeRgba(layout.CanvasWidth, layout.CanvasHeight, rgba);
    }

    private static ProjectedFrame Project(MotionGuideFrameSample sample, double yawDegrees)
    {
        var yaw = yawDegrees * Math.PI / 180d;
        var cos = Math.Cos(yaw);
        var sin = Math.Sin(yaw);
        return new ProjectedFrame(
            sample.FrameIndex,
            sample.TimeSeconds,
            sample.Joints.ToDictionary(
                item => item.Key,
                item =>
                {
                    var point = item.Value;
                    return new ProjectedPoint((point.X * cos) + (point.Z * sin), point.Y);
                },
                StringComparer.OrdinalIgnoreCase),
            sample.Meshes.Select(mesh => new ProjectedMeshPrimitive(
                mesh.Vertices.Select(vertex =>
                {
                    var point = vertex.Position;
                    var normal = vertex.Normal;
                    return new ProjectedMeshVertex(
                        new ProjectedPoint((point.X * cos) + (point.Z * sin), point.Y),
                        (point.Z * cos) - (point.X * sin),
                        (normal.Z * cos) - (normal.X * sin),
                        normal.Y);
                }).ToArray(),
                mesh.Indices,
                mesh.MaterialIndex)).ToArray(),
            sample.Contacts);
    }

    private static string ContactLabel(IReadOnlyList<string> contacts)
    {
        if (contacts.Count == 0)
            return "LIFT";
        var left = contacts.Any(item => item.Contains("left", StringComparison.OrdinalIgnoreCase));
        var right = contacts.Any(item => item.Contains("right", StringComparison.OrdinalIgnoreCase));
        return (left, right) switch
        {
            (true, true) => "L+R",
            (true, false) => "LEFT",
            (false, true) => "RIGHT",
            _ => "LIFT",
        };
    }

    private static void DrawMannequin(
        byte[] rgba,
        double[] zBuffer,
        int width,
        int height,
        IReadOnlyList<ProjectedMeshPrimitive> meshes,
        SlotSpec slot,
        double scale,
        double minY,
        bool diagnostic)
    {
        foreach (var mesh in meshes)
        {
            for (var index = 0; index + 2 < mesh.Indices.Length; index += 3)
            {
                var i0 = mesh.Indices[index];
                var i1 = mesh.Indices[index + 1];
                var i2 = mesh.Indices[index + 2];
                if ((uint)i0 >= mesh.Vertices.Count || (uint)i1 >= mesh.Vertices.Count || (uint)i2 >= mesh.Vertices.Count)
                    continue;

                var v0 = ToScreenVertex(mesh.Vertices[i0], slot, scale, minY);
                var v1 = ToScreenVertex(mesh.Vertices[i1], slot, scale, minY);
                var v2 = ToScreenVertex(mesh.Vertices[i2], slot, scale, minY);
                var shade = TriangleShade(mesh.Vertices[i0], mesh.Vertices[i1], mesh.Vertices[i2]);
                var baseColor = mesh.MaterialIndex == 1
                    ? (R: 136d, G: 154d, B: 176d)
                    : (R: 172d, G: 187d, B: 206d);
                DrawTriangle(
                    rgba,
                    zBuffer,
                    width,
                    height,
                    v0,
                    v1,
                    v2,
                    (byte)Math.Clamp(baseColor.R * shade, 0, 255),
                    (byte)Math.Clamp(baseColor.G * shade, 0, 255),
                    (byte)Math.Clamp(baseColor.B * shade, 0, 255),
                    diagnostic ? (byte)245 : (byte)235);
            }
        }
    }

    private static ScreenVertex ToScreenVertex(ProjectedMeshVertex vertex, SlotSpec slot, double scale, double minY) =>
        new(
            slot.Root.X + (vertex.Point.X * scale),
            slot.BaselineY - ((vertex.Point.Y - minY) * scale),
            vertex.Depth,
            vertex.NormalDepth,
            vertex.NormalY);

    private static double TriangleShade(ProjectedMeshVertex a, ProjectedMeshVertex b, ProjectedMeshVertex c)
    {
        var normalDepth = Math.Abs((a.NormalDepth + b.NormalDepth + c.NormalDepth) / 3d);
        var normalY = Math.Max(0d, (a.NormalY + b.NormalY + c.NormalY) / 3d);
        return Math.Clamp(0.78d + (normalDepth * 0.16d) + (normalY * 0.08d), 0.72d, 1.08d);
    }

    private static void DrawHandEnvelope(byte[] rgba, int width, int height, IReadOnlyDictionary<string, SpriteSheetPoint> screen, SlotSpec slot, bool diagnostic)
    {
        var radius = diagnostic
            ? Math.Max(12, slot.SafeRect.Width / 24)
            : Math.Max(18, slot.SafeRect.Width / 14);
        if (screen.TryGetValue("leftHand", out var left))
            DrawCircle(rgba, width, height, left.X, left.Y, radius, 182, 116, 40, diagnostic ? (byte)80 : (byte)45);
        if (screen.TryGetValue("rightHand", out var right))
            DrawCircle(rgba, width, height, right.X, right.Y, radius, 182, 116, 40, diagnostic ? (byte)80 : (byte)45);
    }

    private static void DrawContactMarker(
        byte[] rgba,
        int width,
        int height,
        IReadOnlyDictionary<string, SpriteSheetPoint> screen,
        IReadOnlyList<string> contacts,
        string contact,
        string footKey,
        string toeKey,
        bool diagnostic)
    {
        if (!contacts.Contains(contact))
            return;
        if (!screen.TryGetValue(footKey, out var foot) && !screen.TryGetValue(toeKey, out foot))
            return;
        var toe = screen.GetValueOrDefault(toeKey, foot);
        DrawThickLine(rgba, width, height, foot.X - 8, Math.Max(foot.Y, toe.Y) + 5, toe.X + 8, Math.Max(foot.Y, toe.Y) + 5, diagnostic ? 4 : 3, 24, 142, 94, diagnostic ? (byte)235 : (byte)155);
    }

    private static Matrix4x4[] ResolveWorldMatrices(IReadOnlyList<GltfNode> nodes, int[] parents, NodePose[] poses)
    {
        var worlds = new Matrix4x4[nodes.Count];
        var resolved = new bool[nodes.Count];
        for (var index = 0; index < nodes.Count; index++)
            Resolve(index);
        return worlds;

        Matrix4x4 Resolve(int index)
        {
            if (resolved[index])
                return worlds[index];

            var local = poses[index].ToMatrix();
            var parent = parents[index];
            worlds[index] = parent < 0 ? local : local * Resolve(parent);
            resolved[index] = true;
            return worlds[index];
        }
    }

    private static int[] ParentMap(IReadOnlyList<GltfNode> nodes)
    {
        var parents = Enumerable.Repeat(-1, nodes.Count).ToArray();
        for (var index = 0; index < nodes.Count; index++)
        {
            foreach (var child in nodes[index].Children ?? [])
            {
                if (child >= 0 && child < parents.Length)
                    parents[child] = index;
            }
        }
        return parents;
    }

    private static SkinnedMesh? LoadSkinnedMesh(GltfBinary gltf, MotionClipDefinition clip)
    {
        var meshNodeIndex = ResolveSkinnedMeshNodeIndex(gltf.Document, clip);
        if (meshNodeIndex is null)
            return null;

        var meshNode = gltf.Document.Nodes[meshNodeIndex.Value];
        if (meshNode.Mesh is not int meshIndex || meshIndex < 0 || meshIndex >= gltf.Document.Meshes.Count)
            return null;
        var skinIndex = clip.SkinIndex ?? meshNode.Skin;
        if (skinIndex is not int resolvedSkinIndex || resolvedSkinIndex < 0 || resolvedSkinIndex >= gltf.Document.Skins.Count)
            return null;

        var skin = gltf.Document.Skins[resolvedSkinIndex];
        var inverseBindMatrices = skin.InverseBindMatrices is int inverseBindAccessor
            ? ReadMatrixAccessor(gltf, inverseBindAccessor)
            : Enumerable.Repeat(Matrix4x4.Identity, skin.Joints.Count).ToArray();
        if (inverseBindMatrices.Length < skin.Joints.Count)
        {
            inverseBindMatrices = inverseBindMatrices
                .Concat(Enumerable.Repeat(Matrix4x4.Identity, skin.Joints.Count - inverseBindMatrices.Length))
                .ToArray();
        }

        var mesh = gltf.Document.Meshes[meshIndex];
        var primitives = new List<SkinnedMeshPrimitive>();
        foreach (var primitive in mesh.Primitives)
        {
            if (!primitive.Attributes.TryGetValue("POSITION", out var positionAccessor)
                || !primitive.Attributes.TryGetValue("JOINTS_0", out var jointsAccessor)
                || !primitive.Attributes.TryGetValue("WEIGHTS_0", out var weightsAccessor)
                || primitive.Indices is not int indicesAccessor)
            {
                continue;
            }

            var positions = ReadVector3Accessor(gltf, positionAccessor);
            var normals = primitive.Attributes.TryGetValue("NORMAL", out var normalAccessor)
                ? ReadVector3Accessor(gltf, normalAccessor)
                : Enumerable.Repeat(Vector3.UnitY, positions.Length).ToArray();
            var joints = ReadVector4IntAccessor(gltf, jointsAccessor);
            var weights = ReadVector4Accessor(gltf, weightsAccessor);
            var indices = ReadIntegerAccessor(gltf, indicesAccessor);
            var vertexCount = new[] { positions.Length, normals.Length, joints.Length, weights.Length }.Min();
            if (vertexCount == 0 || indices.Length < 3)
                continue;

            var vertices = new SkinnedMeshVertex[vertexCount];
            for (var index = 0; index < vertexCount; index++)
            {
                var weight = weights[index];
                var sum = weight.X + weight.Y + weight.Z + weight.W;
                if (sum > 0.0001f)
                    weight /= sum;
                vertices[index] = new SkinnedMeshVertex(
                    positions[index],
                    Vector3.Normalize(normals[index] == Vector3.Zero ? Vector3.UnitY : normals[index]),
                    joints[index],
                    weight);
            }

            primitives.Add(new SkinnedMeshPrimitive(vertices, indices, primitive.Material ?? 0));
        }

        return primitives.Count == 0
            ? null
            : new SkinnedMesh(skin.Joints.ToArray(), inverseBindMatrices, primitives);
    }

    private static int? ResolveSkinnedMeshNodeIndex(GltfDocument document, MotionClipDefinition clip)
    {
        if (!string.IsNullOrWhiteSpace(clip.MeshNodeName))
        {
            for (var index = 0; index < document.Nodes.Count; index++)
            {
                if (string.Equals(document.Nodes[index].Name, clip.MeshNodeName, StringComparison.OrdinalIgnoreCase)
                    && document.Nodes[index].Mesh is not null)
                {
                    return index;
                }
            }
        }

        for (var index = 0; index < document.Nodes.Count; index++)
        {
            if (document.Nodes[index].Mesh is not null && document.Nodes[index].Skin is not null)
                return index;
        }

        return null;
    }

    private static IReadOnlyList<MotionGuideSkinnedPrimitive> SkinMesh(SkinnedMesh mesh, Matrix4x4[] worlds, Vector3 root)
    {
        var jointMatrices = new Matrix4x4[mesh.JointNodeIndices.Length];
        for (var index = 0; index < jointMatrices.Length; index++)
        {
            var nodeIndex = mesh.JointNodeIndices[index];
            var world = nodeIndex >= 0 && nodeIndex < worlds.Length ? worlds[nodeIndex] : Matrix4x4.Identity;
            var inverseBind = index < mesh.InverseBindMatrices.Length ? mesh.InverseBindMatrices[index] : Matrix4x4.Identity;
            jointMatrices[index] = inverseBind * world;
        }

        return mesh.Primitives.Select(primitive =>
        {
            var vertices = new MotionGuideSkinnedVertex[primitive.Vertices.Length];
            for (var index = 0; index < primitive.Vertices.Length; index++)
            {
                var vertex = primitive.Vertices[index];
                var position = Vector3.Zero;
                var normal = Vector3.Zero;
                ApplySkinJoint(vertex.Position, vertex.Normal, vertex.Joints.X, vertex.Weights.X, jointMatrices, ref position, ref normal);
                ApplySkinJoint(vertex.Position, vertex.Normal, vertex.Joints.Y, vertex.Weights.Y, jointMatrices, ref position, ref normal);
                ApplySkinJoint(vertex.Position, vertex.Normal, vertex.Joints.Z, vertex.Weights.Z, jointMatrices, ref position, ref normal);
                ApplySkinJoint(vertex.Position, vertex.Normal, vertex.Joints.W, vertex.Weights.W, jointMatrices, ref position, ref normal);
                if (normal == Vector3.Zero)
                    normal = Vector3.UnitY;
                else
                    normal = Vector3.Normalize(normal);
                vertices[index] = new MotionGuideSkinnedVertex(
                    new Vector3(position.X - root.X, position.Y, position.Z - root.Z),
                    normal);
            }

            return new MotionGuideSkinnedPrimitive(vertices, primitive.Indices, primitive.MaterialIndex);
        }).ToArray();
    }

    private static void ApplySkinJoint(
        Vector3 sourcePosition,
        Vector3 sourceNormal,
        int jointIndex,
        float weight,
        Matrix4x4[] jointMatrices,
        ref Vector3 position,
        ref Vector3 normal)
    {
        if (weight <= 0.0001f || jointIndex < 0 || jointIndex >= jointMatrices.Length)
            return;

        var matrix = jointMatrices[jointIndex];
        position += Vector3.Transform(sourcePosition, matrix) * weight;
        normal += Vector3.TransformNormal(sourceNormal, matrix) * weight;
    }

    private static float[] ReadAccessor(GltfBinary gltf, int accessorIndex)
    {
        if (accessorIndex < 0 || accessorIndex >= gltf.Document.Accessors.Count)
            throw new InvalidOperationException("GLB accessor index is out of range.");

        var accessor = gltf.Document.Accessors[accessorIndex];
        if (accessor.ComponentType != 5126)
            throw new InvalidOperationException("Only float animation accessors are supported.");
        if (accessor.BufferView is not int bufferViewIndex || bufferViewIndex < 0 || bufferViewIndex >= gltf.Document.BufferViews.Count)
            throw new InvalidOperationException("Animation accessor has no buffer view.");

        var bufferView = gltf.Document.BufferViews[bufferViewIndex];
        if ((bufferView.Buffer ?? 0) != 0)
            throw new InvalidOperationException("Only single-buffer GLB files are supported.");

        var componentCount = ComponentCount(accessor.Type);
        var stride = bufferView.ByteStride ?? (componentCount * sizeof(float));
        var start = (bufferView.ByteOffset ?? 0) + (accessor.ByteOffset ?? 0);
        var values = new float[checked(accessor.Count * componentCount)];
        for (var row = 0; row < accessor.Count; row++)
        {
            var sourceOffset = start + (row * stride);
            for (var component = 0; component < componentCount; component++)
            {
                var byteOffset = sourceOffset + (component * sizeof(float));
                values[(row * componentCount) + component] = BitConverter.ToSingle(gltf.BinaryChunk, byteOffset);
            }
        }
        return values;
    }

    private static Vector3[] ReadVector3Accessor(GltfBinary gltf, int accessorIndex)
    {
        var values = ReadAccessor(gltf, accessorIndex);
        var vectors = new Vector3[values.Length / 3];
        for (var index = 0; index < vectors.Length; index++)
            vectors[index] = new Vector3(values[index * 3], values[(index * 3) + 1], values[(index * 3) + 2]);
        return vectors;
    }

    private static Vector4[] ReadVector4Accessor(GltfBinary gltf, int accessorIndex)
    {
        var values = ReadAccessor(gltf, accessorIndex);
        var vectors = new Vector4[values.Length / 4];
        for (var index = 0; index < vectors.Length; index++)
            vectors[index] = new Vector4(values[index * 4], values[(index * 4) + 1], values[(index * 4) + 2], values[(index * 4) + 3]);
        return vectors;
    }

    private static Vector4Int[] ReadVector4IntAccessor(GltfBinary gltf, int accessorIndex)
    {
        var values = ReadIntegerAccessor(gltf, accessorIndex);
        var vectors = new Vector4Int[values.Length / 4];
        for (var index = 0; index < vectors.Length; index++)
            vectors[index] = new Vector4Int(values[index * 4], values[(index * 4) + 1], values[(index * 4) + 2], values[(index * 4) + 3]);
        return vectors;
    }

    private static Matrix4x4[] ReadMatrixAccessor(GltfBinary gltf, int accessorIndex)
    {
        var values = ReadAccessor(gltf, accessorIndex);
        var matrices = new Matrix4x4[values.Length / 16];
        for (var index = 0; index < matrices.Length; index++)
        {
            var offset = index * 16;
            matrices[index] = new Matrix4x4(
                values[offset],
                values[offset + 1],
                values[offset + 2],
                values[offset + 3],
                values[offset + 4],
                values[offset + 5],
                values[offset + 6],
                values[offset + 7],
                values[offset + 8],
                values[offset + 9],
                values[offset + 10],
                values[offset + 11],
                values[offset + 12],
                values[offset + 13],
                values[offset + 14],
                values[offset + 15]);
        }

        return matrices;
    }

    private static int[] ReadIntegerAccessor(GltfBinary gltf, int accessorIndex)
    {
        if (accessorIndex < 0 || accessorIndex >= gltf.Document.Accessors.Count)
            throw new InvalidOperationException("GLB accessor index is out of range.");

        var accessor = gltf.Document.Accessors[accessorIndex];
        if (accessor.BufferView is not int bufferViewIndex || bufferViewIndex < 0 || bufferViewIndex >= gltf.Document.BufferViews.Count)
            throw new InvalidOperationException("Integer accessor has no buffer view.");
        var bufferView = gltf.Document.BufferViews[bufferViewIndex];
        if ((bufferView.Buffer ?? 0) != 0)
            throw new InvalidOperationException("Only single-buffer GLB files are supported.");

        var componentCount = ComponentCount(accessor.Type);
        var componentSize = ComponentSize(accessor.ComponentType);
        var stride = bufferView.ByteStride ?? (componentCount * componentSize);
        var start = (bufferView.ByteOffset ?? 0) + (accessor.ByteOffset ?? 0);
        var values = new int[checked(accessor.Count * componentCount)];
        for (var row = 0; row < accessor.Count; row++)
        {
            var sourceOffset = start + (row * stride);
            for (var component = 0; component < componentCount; component++)
            {
                var byteOffset = sourceOffset + (component * componentSize);
                values[(row * componentCount) + component] = ReadIntegerComponent(gltf.BinaryChunk, byteOffset, accessor.ComponentType);
            }
        }
        return values;
    }

    private static int ComponentCount(string? type) =>
        type switch
        {
            "SCALAR" => 1,
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" => 4,
            "MAT4" => 16,
            _ => throw new InvalidOperationException($"Unsupported accessor type '{type}'."),
        };

    private static int ComponentSize(int componentType) =>
        componentType switch
        {
            5120 or 5121 => 1,
            5122 or 5123 => 2,
            5125 or 5126 => 4,
            _ => throw new InvalidOperationException($"Unsupported accessor component type '{componentType}'."),
        };

    private static int ReadIntegerComponent(byte[] data, int byteOffset, int componentType) =>
        componentType switch
        {
            5120 => unchecked((sbyte)data[byteOffset]),
            5121 => data[byteOffset],
            5122 => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(byteOffset, 2)),
            5123 => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(byteOffset, 2)),
            5125 => unchecked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(byteOffset, 4))),
            _ => throw new InvalidOperationException($"Unsupported integer accessor component type '{componentType}'."),
        };

    private static Vector3 SampleVec3(SampledChannelData data, float time)
    {
        var index = Segment(data.Input, time, out var t);
        var componentCount = data.Output.Length / Math.Max(1, data.Input.Length);
        var a = ReadVec3(data.Output, index, componentCount);
        if (data.Interpolation.Equals("STEP", StringComparison.OrdinalIgnoreCase) || index >= data.Input.Length - 1)
            return a;

        var b = ReadVec3(data.Output, index + 1, componentCount);
        return Vector3.Lerp(a, b, t);
    }

    private static Quaternion SampleRotation(SampledChannelData data, float time)
    {
        var index = Segment(data.Input, time, out var t);
        var componentCount = data.Output.Length / Math.Max(1, data.Input.Length);
        var a = Quaternion.Normalize(ReadQuaternion(data.Output, index, componentCount));
        if (data.Interpolation.Equals("STEP", StringComparison.OrdinalIgnoreCase) || index >= data.Input.Length - 1)
            return a;

        var b = Quaternion.Normalize(ReadQuaternion(data.Output, index + 1, componentCount));
        return Quaternion.Normalize(Quaternion.Slerp(a, b, t));
    }

    private static int Segment(float[] times, float time, out float t)
    {
        if (times.Length <= 1)
        {
            t = 0f;
            return 0;
        }

        if (time <= times[0])
        {
            t = 0f;
            return 0;
        }

        for (var index = 0; index < times.Length - 1; index++)
        {
            if (time <= times[index + 1])
            {
                var duration = Math.Max(0.0001f, times[index + 1] - times[index]);
                t = Math.Clamp((time - times[index]) / duration, 0f, 1f);
                return index;
            }
        }

        t = 0f;
        return times.Length - 1;
    }

    private static Vector3 ReadVec3(float[] values, int index, int componentCount)
    {
        var offset = index * componentCount;
        return new Vector3(
            values.ElementAtOrDefault(offset),
            values.ElementAtOrDefault(offset + 1),
            values.ElementAtOrDefault(offset + 2));
    }

    private static Quaternion ReadQuaternion(float[] values, int index, int componentCount)
    {
        var offset = index * componentCount;
        return new Quaternion(
            values.ElementAtOrDefault(offset),
            values.ElementAtOrDefault(offset + 1),
            values.ElementAtOrDefault(offset + 2),
            values.ElementAtOrDefault(offset + 3));
    }

    private static byte[] NewCanvas(int width, int height, byte r, byte g, byte b, byte a)
    {
        var rgba = new byte[checked(width * height * 4)];
        for (var index = 0; index < rgba.Length; index += 4)
        {
            rgba[index] = r;
            rgba[index + 1] = g;
            rgba[index + 2] = b;
            rgba[index + 3] = a;
        }

        return rgba;
    }

    private static void DrawTriangle(
        byte[] rgba,
        double[] zBuffer,
        int width,
        int height,
        ScreenVertex v0,
        ScreenVertex v1,
        ScreenVertex v2,
        byte r,
        byte g,
        byte b,
        byte a)
    {
        var minX = Math.Max(0, (int)Math.Floor(Math.Min(v0.X, Math.Min(v1.X, v2.X))));
        var maxX = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(v0.X, Math.Max(v1.X, v2.X))));
        var minY = Math.Max(0, (int)Math.Floor(Math.Min(v0.Y, Math.Min(v1.Y, v2.Y))));
        var maxY = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(v0.Y, Math.Max(v1.Y, v2.Y))));
        if (minX > maxX || minY > maxY)
            return;

        var area = Edge(v0.X, v0.Y, v1.X, v1.Y, v2.X, v2.Y);
        if (Math.Abs(area) < 0.0001d)
            return;

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var px = x + 0.5d;
                var py = y + 0.5d;
                var w0 = Edge(v1.X, v1.Y, v2.X, v2.Y, px, py) / area;
                var w1 = Edge(v2.X, v2.Y, v0.X, v0.Y, px, py) / area;
                var w2 = Edge(v0.X, v0.Y, v1.X, v1.Y, px, py) / area;
                if (w0 < -0.0001d || w1 < -0.0001d || w2 < -0.0001d)
                    continue;

                var depth = (v0.Depth * w0) + (v1.Depth * w1) + (v2.Depth * w2);
                var offset = (y * width) + x;
                if (depth < zBuffer[offset])
                    continue;

                zBuffer[offset] = depth;
                Blend(rgba, width, height, x, y, r, g, b, a);
            }
        }
    }

    private static double Edge(double ax, double ay, double bx, double by, double px, double py) =>
        ((px - ax) * (by - ay)) - ((py - ay) * (bx - ax));

    private static void DrawRect(byte[] rgba, int width, int height, SpriteSheetRect rect, byte r, byte g, byte b, byte a, int thickness)
    {
        for (var offset = 0; offset < thickness; offset++)
        {
            DrawLine(rgba, width, height, rect.X, rect.Y + offset, rect.X + rect.Width - 1, rect.Y + offset, r, g, b, a);
            DrawLine(rgba, width, height, rect.X, rect.Y + rect.Height - 1 - offset, rect.X + rect.Width - 1, rect.Y + rect.Height - 1 - offset, r, g, b, a);
            DrawLine(rgba, width, height, rect.X + offset, rect.Y, rect.X + offset, rect.Y + rect.Height - 1, r, g, b, a);
            DrawLine(rgba, width, height, rect.X + rect.Width - 1 - offset, rect.Y, rect.X + rect.Width - 1 - offset, rect.Y + rect.Height - 1, r, g, b, a);
        }
    }

    private static void DrawLine(byte[] rgba, int width, int height, int x1, int y1, int x2, int y2, byte r, byte g, byte b, byte a)
    {
        var dx = Math.Abs(x2 - x1);
        var sx = x1 < x2 ? 1 : -1;
        var dy = -Math.Abs(y2 - y1);
        var sy = y1 < y2 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            Blend(rgba, width, height, x1, y1, r, g, b, a);
            if (x1 == x2 && y1 == y2)
                break;
            var e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x1 += sx;
            }
            if (e2 <= dx)
            {
                err += dx;
                y1 += sy;
            }
        }
    }

    private static void DrawThickLine(byte[] rgba, int width, int height, int x1, int y1, int x2, int y2, int radius, byte r, byte g, byte b, byte a)
    {
        var dx = Math.Abs(x2 - x1);
        var sx = x1 < x2 ? 1 : -1;
        var dy = -Math.Abs(y2 - y1);
        var sy = y1 < y2 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            DrawCircle(rgba, width, height, x1, y1, radius, r, g, b, a);
            if (x1 == x2 && y1 == y2)
                break;
            var e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x1 += sx;
            }
            if (e2 <= dx)
            {
                err += dx;
                y1 += sy;
            }
        }
    }

    private static void DrawCircle(byte[] rgba, int width, int height, int cx, int cy, int radius, byte r, byte g, byte b, byte a)
    {
        var radiusSquared = radius * radius;
        for (var y = cy - radius; y <= cy + radius; y++)
        {
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                if ((dx * dx) + (dy * dy) <= radiusSquared)
                    Blend(rgba, width, height, x, y, r, g, b, a);
            }
        }
    }

    private static void DrawText(byte[] rgba, int width, int height, int x, int y, string text, int scale, byte r, byte g, byte b, byte a)
    {
        var cursor = x;
        foreach (var ch in text)
        {
            var glyph = Glyph(ch);
            for (var gy = 0; gy < glyph.Length; gy++)
            {
                for (var gx = 0; gx < glyph[gy].Length; gx++)
                {
                    if (glyph[gy][gx] != '#')
                        continue;
                    for (var py = 0; py < scale; py++)
                    {
                        for (var px = 0; px < scale; px++)
                            Blend(rgba, width, height, cursor + (gx * scale) + px, y + (gy * scale) + py, r, g, b, a);
                    }
                }
            }
            cursor += (glyph[0].Length + 1) * scale;
        }
    }

    private static string[] Glyph(char ch) =>
        ch switch
        {
            'A' => [" ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #"],
            'B' => ["#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### "],
            'C' => [" ### ", "#   #", "#    ", "#    ", "#    ", "#   #", " ### "],
            'D' => ["#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### "],
            'E' => ["#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####"],
            'F' => ["#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    "],
            'G' => [" ### ", "#   #", "#    ", "# ###", "#   #", "#   #", " ### "],
            'H' => ["#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #"],
            'I' => ["#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "#####"],
            'J' => ["#####", "   # ", "   # ", "   # ", "   # ", "#  # ", " ##  "],
            'K' => ["#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #"],
            'L' => ["#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####"],
            'M' => ["#   #", "## ##", "# # #", "#   #", "#   #", "#   #", "#   #"],
            'N' => ["#   #", "##  #", "# # #", "#  ##", "#   #", "#   #", "#   #"],
            'O' => [" ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### "],
            'P' => ["#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    "],
            'Q' => [" ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #"],
            'R' => ["#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #"],
            'S' => [" ####", "#    ", "#    ", " ### ", "    #", "    #", "#### "],
            'T' => ["#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  "],
            'U' => ["#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### "],
            'V' => ["#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  "],
            'W' => ["#   #", "#   #", "#   #", "# # #", "# # #", "## ##", "#   #"],
            'X' => ["#   #", "#   #", " # # ", "  #  ", " # # ", "#   #", "#   #"],
            'Y' => ["#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  "],
            'Z' => ["#####", "    #", "   # ", "  #  ", " #   ", "#    ", "#####"],
            '0' => [" ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### "],
            '1' => ["  #  ", " ##  ", "# #  ", "  #  ", "  #  ", "  #  ", "#####"],
            '2' => [" ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####"],
            '3' => [" ### ", "#   #", "    #", "  ## ", "    #", "#   #", " ### "],
            '4' => ["   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # "],
            '5' => ["#####", "#    ", "#    ", "#### ", "    #", "#   #", " ### "],
            '6' => [" ### ", "#   #", "#    ", "#### ", "#   #", "#   #", " ### "],
            '7' => ["#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   "],
            '8' => [" ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### "],
            '9' => [" ### ", "#   #", "#   #", " ####", "    #", "#   #", " ### "],
            '.' => ["     ", "     ", "     ", "     ", "     ", " ##  ", " ##  "],
            ':' => ["     ", " ##  ", " ##  ", "     ", " ##  ", " ##  ", "     "],
            '-' => ["     ", "     ", "     ", "#### ", "     ", "     ", "     "],
            '_' => ["     ", "     ", "     ", "     ", "     ", "     ", "#####"],
            '/' => ["    #", "   # ", "   # ", "  #  ", " #   ", " #   ", "#    "],
            '+' => ["     ", "  #  ", "  #  ", "#####", "  #  ", "  #  ", "     "],
            ' ' => ["   ", "   ", "   ", "   ", "   ", "   ", "   "],
            _ => ["#####", "#   #", "   # ", "  #  ", "     ", "  #  ", "     "],
        };

    private static void Blend(byte[] rgba, int width, int height, int x, int y, byte r, byte g, byte b, byte a)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
            return;
        var offset = ((y * width) + x) * 4;
        var sourceAlpha = a / 255d;
        var inverse = 1d - sourceAlpha;
        rgba[offset] = (byte)Math.Clamp((r * sourceAlpha) + (rgba[offset] * inverse), 0, 255);
        rgba[offset + 1] = (byte)Math.Clamp((g * sourceAlpha) + (rgba[offset + 1] * inverse), 0, 255);
        rgba[offset + 2] = (byte)Math.Clamp((b * sourceAlpha) + (rgba[offset + 2] * inverse), 0, 255);
        rgba[offset + 3] = 255;
    }

    private readonly record struct ProjectedPoint(double X, double Y);

    private readonly record struct ScreenVertex(double X, double Y, double Depth, double NormalDepth, double NormalY);

    private readonly record struct Vector4Int(int X, int Y, int Z, int W);

    private sealed record ProjectedMeshVertex(ProjectedPoint Point, double Depth, double NormalDepth, double NormalY);

    private sealed record ProjectedMeshPrimitive(
        IReadOnlyList<ProjectedMeshVertex> Vertices,
        int[] Indices,
        int MaterialIndex);

    private sealed record ProjectedFrame(
        int Index,
        double TimeSeconds,
        IReadOnlyDictionary<string, ProjectedPoint> Points,
        IReadOnlyList<ProjectedMeshPrimitive> Meshes,
        IReadOnlyList<string> Contacts);

    private sealed record SampledChannelData(float[] Input, float[] Output, string Interpolation);

    private sealed record SkinnedMesh(
        int[] JointNodeIndices,
        Matrix4x4[] InverseBindMatrices,
        IReadOnlyList<SkinnedMeshPrimitive> Primitives);

    private sealed record SkinnedMeshPrimitive(
        SkinnedMeshVertex[] Vertices,
        int[] Indices,
        int MaterialIndex);

    private sealed record SkinnedMeshVertex(Vector3 Position, Vector3 Normal, Vector4Int Joints, Vector4 Weights);

    private readonly record struct NodePose(Vector3 Translation, Quaternion Rotation, Vector3 Scale, Matrix4x4? Matrix)
    {
        public static NodePose FromNode(GltfNode node)
        {
            var translation = node.Translation is { Length: >= 3 }
                ? new Vector3(node.Translation[0], node.Translation[1], node.Translation[2])
                : Vector3.Zero;
            var rotation = node.Rotation is { Length: >= 4 }
                ? new Quaternion(node.Rotation[0], node.Rotation[1], node.Rotation[2], node.Rotation[3])
                : Quaternion.Identity;
            var scale = node.Scale is { Length: >= 3 }
                ? new Vector3(node.Scale[0], node.Scale[1], node.Scale[2])
                : Vector3.One;
            Matrix4x4? matrix = null;
            if (node.Matrix is { Length: >= 16 } values)
            {
                matrix = new Matrix4x4(
                    values[0], values[1], values[2], values[3],
                    values[4], values[5], values[6], values[7],
                    values[8], values[9], values[10], values[11],
                    values[12], values[13], values[14], values[15]);
            }

            return new NodePose(translation, rotation, scale, matrix);
        }

        public Matrix4x4 ToMatrix() =>
            Matrix ?? Matrix4x4.CreateScale(Scale)
                * Matrix4x4.CreateFromQuaternion(Rotation)
                * Matrix4x4.CreateTranslation(Translation);
    }

    private sealed class GltfBinary
    {
        private GltfBinary(GltfDocument document, byte[] binaryChunk)
        {
            Document = document;
            BinaryChunk = binaryChunk;
        }

        public GltfDocument Document { get; }
        public byte[] BinaryChunk { get; }

        public static GltfBinary Load(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 20)
                throw new InvalidOperationException("GLB file is too small.");
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)) != 0x46546C67)
                throw new InvalidOperationException("File is not a GLB asset.");
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) != 2)
                throw new InvalidOperationException("Only GLB version 2 is supported.");

            var cursor = 12;
            GltfDocument? document = null;
            byte[]? binary = null;
            while (cursor + 8 <= bytes.Length)
            {
                var chunkLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(cursor, 4));
                var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(cursor + 4, 4));
                cursor += 8;
                if (cursor + chunkLength > bytes.Length)
                    throw new InvalidOperationException("GLB chunk length exceeds file length.");

                var chunk = bytes.AsSpan(cursor, chunkLength);
                if (chunkType == 0x4E4F534A)
                {
                    var json = Encoding.UTF8.GetString(chunk).TrimEnd('\0', ' ', '\r', '\n', '\t');
                    document = JsonSerializer.Deserialize<GltfDocument>(json, JsonOptions);
                }
                else if (chunkType == 0x004E4942)
                {
                    binary = chunk.ToArray();
                }
                cursor += chunkLength;
            }

            return new GltfBinary(
                document ?? throw new InvalidOperationException("GLB JSON chunk was not found."),
                binary ?? throw new InvalidOperationException("GLB binary chunk was not found."));
        }
    }

    private sealed class GltfDocument
    {
        public List<GltfBufferView> BufferViews { get; init; } = [];
        public List<GltfAccessor> Accessors { get; init; } = [];
        public List<GltfNode> Nodes { get; init; } = [];
        public List<GltfMesh> Meshes { get; init; } = [];
        public List<GltfSkin> Skins { get; init; } = [];
        public List<GltfAnimation> Animations { get; init; } = [];
    }

    private sealed class GltfBufferView
    {
        public int? Buffer { get; init; }
        public int? ByteOffset { get; init; }
        public int ByteLength { get; init; }
        public int? ByteStride { get; init; }
    }

    private sealed class GltfAccessor
    {
        public int? BufferView { get; init; }
        public int? ByteOffset { get; init; }
        public int ComponentType { get; init; }
        public int Count { get; init; }
        public string Type { get; init; } = "SCALAR";
    }

    private sealed class GltfNode
    {
        public string? Name { get; init; }
        public int? Mesh { get; init; }
        public int? Skin { get; init; }
        public List<int>? Children { get; init; }
        public float[]? Translation { get; init; }
        public float[]? Rotation { get; init; }
        public float[]? Scale { get; init; }
        public float[]? Matrix { get; init; }
    }

    private sealed class GltfMesh
    {
        public string? Name { get; init; }
        public List<GltfMeshPrimitive> Primitives { get; init; } = [];
    }

    private sealed class GltfMeshPrimitive
    {
        public Dictionary<string, int> Attributes { get; init; } = [];
        public int? Indices { get; init; }
        public int? Material { get; init; }
    }

    private sealed class GltfSkin
    {
        public List<int> Joints { get; init; } = [];
        public int? InverseBindMatrices { get; init; }
        public int? Skeleton { get; init; }
    }

    private sealed class GltfAnimation
    {
        public string? Name { get; init; }
        public List<GltfAnimationSampler> Samplers { get; init; } = [];
        public List<GltfAnimationChannel> Channels { get; init; } = [];
    }

    private sealed class GltfAnimationSampler
    {
        public int Input { get; init; }
        public int Output { get; init; }
        public string? Interpolation { get; init; }
    }

    private sealed class GltfAnimationChannel
    {
        public int Sampler { get; init; }
        public GltfAnimationTarget? Target { get; init; }
    }

    private sealed class GltfAnimationTarget
    {
        public int? Node { get; init; }
        public string? Path { get; init; }
    }
}

internal sealed record MotionGuideRenderResult(
    byte[] GuidePng,
    byte[] DiagnosticPng,
    MotionGuideRenderMetadata Metadata,
    IReadOnlyList<MotionGuideFrameSample> Samples);

internal sealed record MotionGuideRenderMetadata(
    string Renderer,
    string RenderStyle,
    string ClipId,
    string AnimationName,
    string SourcePackage,
    string SourceUrl,
    string SourceLicense,
    string Facing,
    double CameraYawDegrees,
    int SampleCount,
    string AssetPath);

internal sealed record MotionGuideFrameSample(
    int FrameIndex,
    double TimeSeconds,
    IReadOnlyDictionary<string, Vector3> Joints,
    IReadOnlyList<MotionGuideSkinnedPrimitive> Meshes,
    IReadOnlyList<string> Contacts);

internal readonly record struct MotionGuideSkinnedVertex(Vector3 Position, Vector3 Normal);

internal sealed record MotionGuideSkinnedPrimitive(
    IReadOnlyList<MotionGuideSkinnedVertex> Vertices,
    int[] Indices,
    int MaterialIndex);
