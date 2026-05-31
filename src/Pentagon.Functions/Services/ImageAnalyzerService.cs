using Google.Cloud.Vision.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pentagon.Functions.Models;

namespace Pentagon.Functions.Services;

public interface IImageAnalyzerService
{
    Task<ProcessImageResult> AnalyzeAsync(ProcessImageRequest request, CancellationToken cancellationToken = default);
}

public sealed class ImageAnalyzerService : IImageAnalyzerService
{
    private const string MotorcycleLabel = "Motorcycle";
    private const double DefaultConfidenceThreshold = 0.8;

    private static readonly HashSet<string> RelevantVehicleLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Motorcycle",
        "Motorbike",
    };

    private readonly GoogleVisionCredentialProvider _credentialProvider;
    private readonly double _confidenceThreshold;
    private readonly ILogger<ImageAnalyzerService> _logger;

    public ImageAnalyzerService(
        GoogleVisionCredentialProvider credentialProvider,
        IConfiguration configuration,
        ILogger<ImageAnalyzerService> logger)
    {
        _credentialProvider = credentialProvider;
        _logger = logger;
        _confidenceThreshold = double.TryParse(
            configuration["GoogleVisionConfidenceThreshold"],
            out var threshold)
            ? threshold
            : DefaultConfidenceThreshold;
    }

    public async Task<ProcessImageResult> AnalyzeAsync(
        ProcessImageRequest request,
        CancellationToken cancellationToken = default)
    {
        var client = await _credentialProvider.GetClientAsync();

        var image = new Image { Source = new ImageSource { ImageUri = request.ImageUrl } };
        var annotateRequest = new AnnotateImageRequest
        {
            Image = image,
            Features =
            {
                new Feature { Type = Feature.Types.Type.FaceDetection },
                new Feature { Type = Feature.Types.Type.LabelDetection, MaxResults = 20 },
                new Feature { Type = Feature.Types.Type.ObjectLocalization, MaxResults = 20 },
            },
        };

        var batchRequest = new BatchAnnotateImagesRequest
        {
            Requests = { annotateRequest },
        };

        _logger.LogInformation("Analyzing image at {ImageUrl}", request.ImageUrl);

        var response = await client.BatchAnnotateImagesAsync(batchRequest, cancellationToken: cancellationToken);
        var annotation = response.Responses[0];

        if (annotation.Error is { Message: { Length: > 0 } errorMessage })
        {
            throw new InvalidOperationException($"Google Vision API error: {errorMessage}");
        }

        var faceAnnotations = annotation.FaceAnnotations;
        var labelAnnotations = annotation.LabelAnnotations;
        var objectAnnotations = annotation.LocalizedObjectAnnotations;

        var motorcycleLabel = labelAnnotations
            .Where(l => string.Equals(l.Description, MotorcycleLabel, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(l => l.Score)
            .FirstOrDefault();

        var motorcycleObject = objectAnnotations
            .Where(o => string.Equals(o.Name, MotorcycleLabel, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(o => o.Score)
            .FirstOrDefault();

        var motorcycleLabelDetected = motorcycleLabel?.Score >= _confidenceThreshold;
        var motorcycleObjectDetected = motorcycleObject?.Score >= _confidenceThreshold;
        var motorcycleDetected = motorcycleLabelDetected || motorcycleObjectDetected;

        double? motorcycleConfidence = null;
        if (motorcycleObjectDetected && motorcycleLabelDetected)
        {
            motorcycleConfidence = Math.Max(motorcycleObject!.Score, motorcycleLabel!.Score);
        }
        else if (motorcycleObjectDetected)
        {
            motorcycleConfidence = motorcycleObject!.Score;
        }
        else if (motorcycleLabelDetected)
        {
            motorcycleConfidence = motorcycleLabel!.Score;
        }

        var faceDetected = faceAnnotations.Count > 0;
        var pass = faceDetected && motorcycleDetected;

        var vision = BuildTrimmedVisionResponse(
            faceAnnotations,
            labelAnnotations,
            motorcycleLabel,
            motorcycleObject,
            motorcycleLabelDetected,
            motorcycleObjectDetected);

        return new ProcessImageResult
        {
            Result = pass ? "Pass" : "Fail",
            FaceDetected = faceDetected,
            MotorcycleDetected = motorcycleDetected,
            MotorcycleConfidence = motorcycleConfidence,
            Vision = vision,
        };
    }

    private TrimmedVisionResponse BuildTrimmedVisionResponse(
        IReadOnlyList<FaceAnnotation> faceAnnotations,
        IReadOnlyList<EntityAnnotation> labelAnnotations,
        EntityAnnotation? motorcycleLabel,
        LocalizedObjectAnnotation? motorcycleObject,
        bool motorcycleLabelDetected,
        bool motorcycleObjectDetected)
    {
        var faces = faceAnnotations
            .Select(f => new FaceSummary
            {
                DetectionConfidence = f.DetectionConfidence,
                BoundingPoly = MapBoundingPoly(f.BoundingPoly),
            })
            .ToList();

        MotorcycleDetection? motorcycle = null;
        if (motorcycleObjectDetected && motorcycleLabelDetected)
        {
            motorcycle = motorcycleObject!.Score >= motorcycleLabel!.Score
                ? MapMotorcycleFromObject(motorcycleObject)
                : MapMotorcycleFromLabel(motorcycleLabel);
        }
        else if (motorcycleObjectDetected)
        {
            motorcycle = MapMotorcycleFromObject(motorcycleObject!);
        }
        else if (motorcycleLabelDetected)
        {
            motorcycle = MapMotorcycleFromLabel(motorcycleLabel!);
        }

        var otherRelevantLabels = labelAnnotations
            .Where(l => RelevantVehicleLabels.Contains(l.Description))
            .Where(l => !string.Equals(l.Description, MotorcycleLabel, StringComparison.OrdinalIgnoreCase))
            .Where(l => l.Score >= _confidenceThreshold)
            .OrderByDescending(l => l.Score)
            .Select(l => new LabelSummary
            {
                Description = l.Description,
                Score = l.Score,
            })
            .ToList();

        return new TrimmedVisionResponse
        {
            FaceCount = faces.Count,
            Faces = faces,
            Motorcycle = motorcycle,
            OtherRelevantLabels = otherRelevantLabels,
        };
    }

    private static MotorcycleDetection MapMotorcycleFromObject(LocalizedObjectAnnotation annotation) =>
        new()
        {
            Source = "objectLocalization",
            Name = annotation.Name,
            Score = annotation.Score,
            BoundingPoly = MapBoundingPoly(annotation.BoundingPoly),
        };

    private static MotorcycleDetection MapMotorcycleFromLabel(EntityAnnotation annotation) =>
        new()
        {
            Source = "labelDetection",
            Name = annotation.Description,
            Score = annotation.Score,
        };

    private static Models.BoundingPoly? MapBoundingPoly(Google.Cloud.Vision.V1.BoundingPoly? poly)
    {
        if (poly is null || poly.Vertices.Count == 0)
        {
            return null;
        }

        return new Models.BoundingPoly
        {
            Vertices = poly.Vertices
                .Select(v => new Models.Vertex { X = v.X, Y = v.Y })
                .ToList(),
        };
    }
}
