// Compiler-support attributes the library's C# needs that netstandard2.0 / 2.1 do not define (all of them exist from .NET 7).
// Internal, and a separate file because it declares namespaces other than Mermaider.Compat.
#if NETSTANDARD
namespace System.Runtime.CompilerServices
{
	internal static class IsExternalInit;

	[AttributeUsage(AttributeTargets.Parameter)]
	internal sealed class CallerArgumentExpressionAttribute(string parameterName) : Attribute
	{
		public string ParameterName { get; } = parameterName;
	}

	[AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
	internal sealed class CompilerFeatureRequiredAttribute(string featureName) : Attribute
	{
		public string FeatureName { get; } = featureName;
		public bool IsOptional { get; init; }
	}

	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
	internal sealed class RequiredMemberAttribute : Attribute;
}

namespace System.Diagnostics.CodeAnalysis
{
	[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
	internal sealed class SetsRequiredMembersAttribute : Attribute;
}
#endif
