namespace NlcsLegenda.Core;

// Een legenda-regel die niet (volledig) getekend kon worden. Zo verdwijnt een renderfout
// niet stil terwijl de update toch als volledig geslaagd zou gelden.
public sealed record RenderIssue(string Entry, string SourceLayer, string Kind, string Reason);
