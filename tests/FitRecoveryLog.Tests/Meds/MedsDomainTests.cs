using FitRecoveryLog.Domain.Labs;
using FitRecoveryLog.Domain.Meds;
using FitRecoveryLog.Domain.Notes;
using NUnit.Framework;

namespace FitRecoveryLog.Tests.Meds;

[TestFixture]
public class MedsDomainTests
{
    [Test]
    public void MedicationDose_RequiresName()
    {
        Assert.Throws<ArgumentException>(() => MedicationDose.Create(DateTime.Now, "  "));
    }

    [Test]
    public void MedicationDose_TrimsNameAndKeepsDetail()
    {
        var d = MedicationDose.Create(DateTime.Now, "  Testosterone ");
        d.SetInjectionSite("left delt");
        Assert.Multiple(() =>
        {
            Assert.That(d.Name, Is.EqualTo("Testosterone"));
            Assert.That(d.InjectionSite, Is.EqualTo("left delt"));
        });
    }

    [Test]
    public void Lab_RequiresName()
    {
        Assert.Throws<ArgumentException>(() => LabResult.Create(new DateOnly(2026, 8, 5), ""));
    }

    [Test]
    public void Note_RequiresText()
    {
        Assert.Throws<ArgumentException>(() => Note.Create(DateTime.Now, "   "));
    }

    [Test]
    public void Note_TrimsText()
    {
        var n = Note.Create(DateTime.Now, "  felt great after workout ");
        Assert.That(n.Text, Is.EqualTo("felt great after workout"));
    }
}
