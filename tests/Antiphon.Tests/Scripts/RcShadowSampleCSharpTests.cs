using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1836 S6c. Same witness suffixes as RcIncrementalShadowTests. Red until the compiled type exists.
[Category("Unit")]
public sealed class RcShadowSampleCSharpTests
{
    [Test]
    public void SampleSizeStrataAndReplay()
    {
        RcPortCorpus.RequireShadow();
        RcPortCorpus.Replay("SampleSizeStrataAndReplay");
    }

    [Test]
    public void MismatchInvalidatesCarries()
    {
        RcPortCorpus.RequireShadow();
        RcPortCorpus.Replay("MismatchInvalidatesCarries");
    }

    [Test]
    public void NoCarryAndOnePilotDoNotGrantTrust()
    {
        RcPortCorpus.RequireShadow();
        RcPortCorpus.Replay("NoCarryAndOnePilotDoNotGrantTrust");
    }

    [Test]
    public void OneExecutedSampleIsNotAPilot()
    {
        RcPortCorpus.RequireShadow();
        RcPortCorpus.Replay("OneExecutedSampleIsNotAPilot");
    }

    [Test]
    public void EmptyDigestIsUnproven()
    {
        RcPortCorpus.RequireShadow();
        RcPortCorpus.Replay("EmptyDigestIsUnproven");
    }

    [Test]
    public void MissingCarryableIsDirty()
    {
        RcPortCorpus.RequireShadow();
        RcPortCorpus.Replay("MissingCarryableIsDirty");
    }

    [Test]
    public void EvidenceDerivesSampleFailure()
    {
        RcPortCorpus.RequireShadow();
        RcPortCorpus.Replay("EvidenceDerivesSampleFailure");
    }

    [Test]
    public void NonBooleanProvenanceFailureInvalidates()
    {
        RcPortCorpus.RequireShadow();
        RcPortCorpus.Replay("NonBooleanProvenanceFailureInvalidates");
    }

    [Test]
    public void CarriedChunkResultInvalidatesCarries()
    {
        RcPortCorpus.RequireShadow();
        RcPortCorpus.Replay("CarriedChunkResultInvalidatesCarries");
    }

    [Test]
    public void AbsentEvidenceKeepsCarriesUntilWired()
    {
        RcPortCorpus.RequireShadow();
        RcPortCorpus.Replay("AbsentEvidenceKeepsCarriesUntilWired");
    }

    [Test]
    public void EmptyCollectionFailsClosed()
    {
        RcPortCorpus.RequireShadow();
        RcPortCorpus.Replay("EmptyCollectionFailsClosed");
    }

    [Test]
    public void DictionaryKeyShadowFailsClosed()
    {
        RcPortCorpus.RequireShadow();
        RcPortCorpus.Replay("DictionaryKeyShadowFailsClosed");
    }

    // CARD-2010. Expected output is the pre-port PowerShell result.
    [Test]
    public void PsObjectWrappedChunkId()
    {
        RcPortCorpus.RequireShadow();
        RcPortCorpus.Replay("PsObjectWrappedChunkId");
    }

    [Test]
    public void PsObjectWrappedArrayOfOne()
    {
        RcPortCorpus.RequireShadow();
        RcPortCorpus.Replay("PsObjectWrappedArrayOfOne");
    }

    [Test]
    public void ContractDictionaryKeysEntry()
    {
        RcPortCorpus.RequireShadow();
        RcPortCorpus.Replay("ContractDictionaryKeysEntry");
    }
}
