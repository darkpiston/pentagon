namespace Pentagon.Functions.Models;

public sealed class ProcessImageResult
{
    public required string Result { get; init; }

    public bool FaceDetected { get; init; }

    public bool MotorcycleDetected { get; init; }

    public double? MotorcycleConfidence { get; init; }

    public required TrimmedVisionResponse Vision { get; init; }
}
