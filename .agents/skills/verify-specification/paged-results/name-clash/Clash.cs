// The control: imports OpenSpecification.Paging beside the consumer's own namespace, so Pagination is ambiguous. Against
// a tree with #59 the line marked CLASH fails with CS0104, which shows the check can see a clash. Copy it into a
// consumer of its own.
using OpenSpecification.Paging;
using Consumer.Web;

public static class Clash
{
    public static int Total(Pagination pagination) => pagination.TotalItems; // CLASH
}
