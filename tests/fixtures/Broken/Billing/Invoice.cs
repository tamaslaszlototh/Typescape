using Ledger;
using Vendor.Tax;

namespace Billing;

public sealed class Invoice(TaxTable table)
{
    private readonly List<Entry> _entries = [];

    public void Add(Entry entry) => _entries.Add(entry);

    public decimal Total()
    {
        var net = _entries.Sum(entry => entry.Amount);
        TaxRate rate = table.RateFor(net);
        return net + rate.Apply(net);
    }
}
