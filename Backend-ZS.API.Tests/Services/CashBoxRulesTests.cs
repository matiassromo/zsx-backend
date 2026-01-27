using FluentAssertions;
using Xunit;

namespace Backend_ZS.API.Tests.Services
{
    public static class CashBoxRules
    {
        public static decimal CalculateClosingBalance(decimal opening, decimal payments)
        {
            if (opening < 0 || payments < 0)
                throw new ArgumentOutOfRangeException();

            return opening + payments;
        }
    }

    public class CashBoxRulesTests
    {
        [Fact]
        public void CalculateClosingBalance_ReturnsCorrectSum()
        {
            // Arrange
            var opening = 100m;
            var payments = 50m;

            // Act
            var result = CashBoxRules.CalculateClosingBalance(opening, payments);

            // Assert
            result.Should().Be(150m);
        }

        [Fact]
        public void CalculateClosingBalance_WhenNegative_Throws()
        {
            var act = () => CashBoxRules.CalculateClosingBalance(-1, 10);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
