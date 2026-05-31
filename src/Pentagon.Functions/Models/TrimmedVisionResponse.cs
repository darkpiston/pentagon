namespace Pentagon.Functions.Models;

public sealed class TrimmedVisionResponse
{
    public int FaceCount { get; init; }

    public IReadOnlyList<FaceSummary> Faces { get; init; } = [];

    public MotorcycleDetection? Motorcycle { get; init; }

    public IReadOnlyList<LabelSummary> Labels { get; init; } = [];

    public IReadOnlyList<LabelSummary> OtherRelevantLabels { get; init; } = [];
}

public sealed class FaceSummary
{
    public float? DetectionConfidence { get; init; }

    public BoundingPoly? BoundingPoly { get; init; }
}

public sealed class MotorcycleDetection
{
    public required string Source { get; init; }

    public required string Name { get; init; }

    public float Score { get; init; }

    public BoundingPoly? BoundingPoly { get; init; }
}

public sealed class LabelSummary
{
    public required string Description { get; init; }

    public float Score { get; init; }
}

public sealed class BoundingPoly
{
    public IReadOnlyList<Vertex> Vertices { get; init; } = [];
}

public sealed class Vertex
{
    public int? X { get; init; }

    public int? Y { get; init; }
}
