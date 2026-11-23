namespace Holograma3D.Core.Domain.Models;

/// <summary>El archivo no es un modelo aceptable; el mensaje es para el usuario.</summary>
public sealed class ModelValidationException(string message) : Exception(message);
