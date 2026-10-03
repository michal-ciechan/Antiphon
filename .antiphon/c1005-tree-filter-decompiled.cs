using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Helpers;
using Microsoft.Testing.Platform.Resources;
using Polyfills;

namespace Microsoft.Testing.Platform.Requests;

/// <summary>
/// A tree based filter for test execution.
/// </summary>
[Experimental("TPEXP", UrlFormat = "https://aka.ms/testingplatform/diagnostics#{0}")]
public sealed class TreeNodeFilter : ITestExecutionFilter
{
	/// <summary>
	/// The path separator character.
	/// </summary>
	public const char PathSeparator = '/';

	internal const string AllNodesBelowRegexString = ".*.*";

	private readonly List<FilterExpression> _filters;

	/// <summary>
	/// Gets the filter string.
	/// </summary>
	public string Filter { get; }

	internal TreeNodeFilter(string filter)
	{
		Filter = Ensure.NotNull(filter, "filter");
		_filters = ParseFilter(filter);
	}

	/// <remarks>
	/// The current grammar for the filter looks as follows:
	/// <code>
	/// TREE_NODE_FILTER = EXPR ( '/' EXPR )*
	/// EXPR =
	///   '(' EXPR ')'
	///   | EXPR OP EXPR
	///   | NODE_VALUE
	/// FILTER_EXPR =
	///   '(' FILTER_EXPR ')'
	///   | TOKEN '=' TOKEN
	///   | TOKEN '!=' TOKEN
	///   | FILTER_EXPR OP FILTER_EXPR
	///   | TOKEN
	/// OP = '&amp;' | '|'
	/// NODE_VALUE = TOKEN | TOKEN '[' FILTER_EXPR ']'
	/// TOKEN = string
	/// </code>
	/// </remarks>
	/// <exception cref="T:System.InvalidOperationException">
	/// Exception thrown, if the filter is malformed, for example <c>A(|B)</c> or <c>A)</c>.
	/// </exception>
	private static List<FilterExpression> ParseFilter(string filter)
	{
		Stack<FilterExpression> stack = new Stack<FilterExpression>();
		Stack<OperatorKind> stack2 = new Stack<OperatorKind>();
		bool flag = false;
		bool flag2 = false;
		bool flag3 = false;
		using (IEnumerator<string> enumerator = TokenizeFilter(filter).GetEnumerator())
		{
			string current;
			for (; enumerator.MoveNext(); flag3 = current == "(")
			{
				current = enumerator.Current;
				string text = current;
				if (text != null)
				{
					switch (text.Length)
					{
					case 1:
						switch (text[0])
						{
						case '&':
						case '|':
						{
							if (!flag)
							{
								throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, PlatformResources.TreeNodeFilterOperatorNotAllowedErrorMessage, filter));
							}
							OperatorKind operatorKind2;
							if (!(current == "&"))
							{
								if (!(current == "|"))
								{
									throw ApplicationStateGuard.Unreachable("/_/src/Platform/Microsoft.Testing.Platform/Requests/TreeNodeFilter/TreeNodeFilter.cs", 97);
								}
								operatorKind2 = OperatorKind.Or;
							}
							else
							{
								operatorKind2 = OperatorKind.And;
							}
							OperatorKind currentOp = operatorKind2;
							ProcessHigherPrecedenceOperators(stack, stack2, currentOp);
							flag = false;
							flag2 = false;
							continue;
						}
						case '/':
							ProcessHigherPrecedenceOperators(stack, stack2, OperatorKind.Separator);
							flag = false;
							flag2 = false;
							continue;
						case '(':
							stack2.Push(OperatorKind.LeftBrace);
							flag = false;
							flag2 = false;
							continue;
						case ')':
							if (!flag)
							{
								throw new InvalidOperationException();
							}
							while (true)
							{
								if (stack2.Count == 0)
								{
									throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, PlatformResources.TreeNodeFilterUnbalancedFilterErrorMessage, filter, '(', ')'));
								}
								OperatorKind operatorKind = stack2.Pop();
								if (operatorKind == OperatorKind.LeftBrace)
								{
									break;
								}
								ProcessStackOperator(operatorKind, stack, stack2);
							}
							flag = true;
							flag2 = false;
							continue;
						case ']':
						{
							if (!flag)
							{
								throw new InvalidOperationException();
							}
							while (true)
							{
								if (stack2.Count == 0)
								{
									throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, PlatformResources.TreeNodeFilterUnbalancedFilterErrorMessage, filter, '[', ']'));
								}
								OperatorKind operatorKind = stack2.Pop();
								if (operatorKind == OperatorKind.LeftParameter)
								{
									break;
								}
								ProcessStackOperator(operatorKind, stack, stack2);
							}
							FilterExpression properties = stack.Pop();
							FilterExpression filterExpression = stack.Pop();
							if (!(filterExpression is ValueExpression value))
							{
								throw new InvalidOperationException();
							}
							stack.Push(new ValueAndPropertyExpression(value, properties));
							flag2 = false;
							flag = true;
							continue;
						}
						case '[':
							if (!flag2)
							{
								throw new InvalidOperationException();
							}
							stack2.Push(OperatorKind.LeftParameter);
							flag = false;
							flag2 = false;
							continue;
						case '=':
							stack2.Push(OperatorKind.FilterEquals);
							flag = false;
							flag2 = false;
							continue;
						case '!':
							if (flag3)
							{
								stack2.Push(OperatorKind.UnaryNot);
								continue;
							}
							break;
						}
						break;
					case 2:
						if (!(text == "!="))
						{
							break;
						}
						stack2.Push(OperatorKind.FilterNotEquals);
						flag = false;
						flag2 = false;
						continue;
					}
				}
				stack.Push(new ValueExpression(current));
				flag = true;
				flag2 = true;
			}
		}
		while (stack2.Count > 0 && stack2.Peek() != OperatorKind.Separator)
		{
			OperatorKind operatorKind = stack2.Pop();
			ProcessStackOperator(operatorKind, stack, stack2);
		}
		List<FilterExpression> list = stack.Reverse().ToList();
		for (int i = 0; i < list.Count; i++)
		{
			ValidateExpression(list[i], i == list.Count - 1);
		}
		return list;
		static void ProcessHigherPrecedenceOperators(Stack<FilterExpression> expressionStack, Stack<OperatorKind> operatorStack, OperatorKind operatorKind3)
		{
			if (operatorStack.Count != 0 && operatorStack.Peek() > operatorKind3)
			{
				OperatorKind op = operatorStack.Pop();
				ProcessStackOperator(op, expressionStack, operatorStack);
			}
			operatorStack.Push(operatorKind3);
		}
	}

	private static void ValidateExpression(FilterExpression expr, bool isMatchAllAllowed)
	{
		if (expr is OperatorExpression operatorExpression)
		{
			switch (operatorExpression.Op)
			{
			case FilterOperator.Not:
			{
				IReadOnlyCollection<FilterExpression> subExpressions = operatorExpression.SubExpressions;
				if (subExpressions.Count == 1)
				{
					break;
				}
				goto IL_007b;
			}
			case FilterOperator.And:
			{
				IReadOnlyCollection<FilterExpression> subExpressions = operatorExpression.SubExpressions;
				if (subExpressions == null)
				{
					break;
				}
				int count = subExpressions.Count;
				if (count >= 2)
				{
					break;
				}
				goto IL_007b;
			}
			case FilterOperator.Or:
				{
					IReadOnlyCollection<FilterExpression> subExpressions = operatorExpression.SubExpressions;
					if (subExpressions == null)
					{
						break;
					}
					int count = subExpressions.Count;
					if (count >= 2)
					{
						break;
					}
					goto IL_007b;
				}
				IL_007b:
				throw ApplicationStateGuard.Unreachable("/_/src/Platform/Microsoft.Testing.Platform/Requests/TreeNodeFilter/TreeNodeFilter.cs", 288);
			}
			{
				foreach (FilterExpression subExpression in operatorExpression.SubExpressions)
				{
					ValidateExpression(subExpression, isMatchAllAllowed);
				}
				return;
			}
		}
		if (expr is ValueExpression valueExpression)
		{
			if (valueExpression.Value.Contains('/'))
			{
				throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, PlatformResources.TreeNodeFilterCannotContainSlashCharacterErrorMessage, valueExpression.Value));
			}
			ValueExpression valueExpression2 = valueExpression;
			if (valueExpression2.Value.Equals(".*.*", StringComparison.Ordinal) && !isMatchAllAllowed)
			{
				throw new ArgumentException(PlatformResources.TreeNodeFilterOnlyLastLevelCanContainMutiLevelWildcardErrorMessage);
			}
		}
	}

	private static void ProcessStackOperator(OperatorKind op, Stack<FilterExpression> expr, Stack<OperatorKind> ops)
	{
		switch (op)
		{
		case OperatorKind.Or:
		case OperatorKind.And:
		{
			int num = 2;
			List<FilterExpression> list = new List<FilterExpression>(num);
			CollectionsMarshal.SetCount(list, num);
			Span<FilterExpression> span = CollectionsMarshal.AsSpan(list);
			span[0] = expr.Pop();
			span[1] = expr.Pop();
			List<FilterExpression> list2 = list;
			while (ops.Count > 0 && ops.Peek() == op)
			{
				ops.Pop();
				list2.Add(expr.Pop());
			}
			expr.Push(new OperatorExpression(op switch
			{
				OperatorKind.And => FilterOperator.And, 
				OperatorKind.Or => FilterOperator.Or, 
				_ => throw ApplicationStateGuard.Unreachable("/_/src/Platform/Microsoft.Testing.Platform/Requests/TreeNodeFilter/TreeNodeFilter.cs", 332), 
			}, list2));
			break;
		}
		case OperatorKind.FilterEquals:
		case OperatorKind.FilterNotEquals:
		{
			FilterExpression filterExpression = expr.Pop();
			FilterExpression filterExpression2 = expr.Pop();
			if (!(filterExpression2 is ValueExpression propertyName) || !(filterExpression is ValueExpression value))
			{
				throw new InvalidOperationException();
			}
			FilterExpression item2 = new PropertyExpression(propertyName, value);
			if (op == OperatorKind.FilterNotEquals)
			{
				item2 = new OperatorExpression(FilterOperator.Not, new <>z__ReadOnlySingleElementList<FilterExpression>(item2));
			}
			expr.Push(item2);
			break;
		}
		case OperatorKind.UnaryNot:
		{
			FilterExpression item = expr.Pop();
			expr.Push(new OperatorExpression(FilterOperator.Not, new <>z__ReadOnlySingleElementList<FilterExpression>(item)));
			break;
		}
		default:
			throw new InvalidOperationException(PlatformResources.TreeNodeFilterUnexpectedSlashOperatorErrorMessage);
		}
	}

	private static IEnumerable<string> TokenizeFilter(string filter)
	{
		int i = 0;
		StringBuilder lastStringTokenBuilder = new StringBuilder();
		int openedSquareBrackets = 0;
		for (; i < filter.Length; i++)
		{
			switch (filter[i])
			{
			case '\\':
				if (i + 1 < filter.Length)
				{
					lastStringTokenBuilder.Append(Regex.Escape(filter[i + 1].ToString()));
					i++;
					continue;
				}
				throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, PlatformResources.TreeNodeFilterEscapeCharacterShouldNotBeLastErrorMessage, filter));
			case '*':
				lastStringTokenBuilder.Append(".*");
				continue;
			case '[':
				openedSquareBrackets++;
				goto case '&';
			case ']':
				openedSquareBrackets--;
				goto case '&';
			case '/':
				if (openedSquareBrackets > 0)
				{
					lastStringTokenBuilder.Append(filter[i]);
					continue;
				}
				goto case '&';
			case '&':
			case '(':
			case ')':
			case '=':
			case '|':
				if (lastStringTokenBuilder.Length > 0)
				{
					yield return lastStringTokenBuilder.ToString();
					lastStringTokenBuilder.Clear();
				}
				yield return filter[i].ToString();
				continue;
			case '!':
				if (i + 1 < filter.Length && filter[i + 1] == '=')
				{
					if (lastStringTokenBuilder.Length > 0)
					{
						yield return lastStringTokenBuilder.ToString();
						lastStringTokenBuilder.Clear();
					}
					yield return "!=";
					i++;
					continue;
				}
				if (i - 1 >= 0 && filter[i - 1] == '(')
				{
					yield return "!";
					continue;
				}
				break;
			}
			lastStringTokenBuilder.Append(Regex.Escape(filter[i].ToString()));
		}
		if (lastStringTokenBuilder.Length > 0)
		{
			yield return lastStringTokenBuilder.ToString();
		}
	}

	/// <summary>
	/// Checks whether a node path matches the tree node filter.
	/// </summary>
	/// <param name="testNodeFullPath">The segment URL encoded path.</param>
	/// <param name="filterableProperties">The URL encoded node properties.</param>
	public bool MatchesFilter(string testNodeFullPath, PropertyBag filterableProperties)
	{
		Ensure.NotNullOrEmpty(testNodeFullPath, "testNodeFullPath");
		ArgumentGuard.Ensure(testNodeFullPath[0] == '/', "testNodeFullPath", string.Format(CultureInfo.InvariantCulture, PlatformResources.TreeNodeFilterPathShouldStartWithSlashErrorMessage, '/'));
		int num = 1;
		int num2 = 0;
		while (true)
		{
			int num3 = testNodeFullPath.IndexOf('/', num);
			if (num2 >= _filters.Count)
			{
				FilterExpression filterExpression = _filters.Last();
				if (filterExpression is ValueAndPropertyExpression valueAndPropertyExpression)
				{
					filterExpression = valueAndPropertyExpression.Value;
				}
				if (num2 > 0)
				{
					if (filterExpression is ValueExpression valueExpression)
					{
						return valueExpression.Value == ".*.*";
					}
					return false;
				}
				return false;
			}
			if (!MatchFilterPattern(_filters[num2], testNodeFullPath, num, (num3 == -1) ? testNodeFullPath.Length : num3, filterableProperties))
			{
				return false;
			}
			num2++;
			if (num3 < 0)
			{
				break;
			}
			num = num3 + 1;
		}
		return true;
	}

	private static bool MatchFilterPattern(FilterExpression filterExpression, string testNodeFullPath, int startFragmentIndex, int endFragmentIndex, PropertyBag properties)
	{
		string testNodeFragment = testNodeFullPath.Substring(startFragmentIndex, endFragmentIndex - startFragmentIndex);
		return MatchFilterPattern(filterExpression, testNodeFragment, properties);
	}

	private static bool MatchFilterPattern(FilterExpression filterExpression, string testNodeFragment, PropertyBag properties)
	{
		if (!(filterExpression is ValueExpression valueExpression))
		{
			if (filterExpression is OperatorExpression operatorExpression)
			{
				switch (operatorExpression.Op)
				{
				case FilterOperator.Or:
				{
					IReadOnlyCollection<FilterExpression> subExpressions = operatorExpression.SubExpressions;
					return subExpressions.Any((FilterExpression expr) => MatchFilterPattern(expr, testNodeFragment, properties));
				}
				case FilterOperator.And:
				{
					IReadOnlyCollection<FilterExpression> subExpressions = operatorExpression.SubExpressions;
					IReadOnlyCollection<FilterExpression> source2 = subExpressions;
					return source2.All((FilterExpression expr) => MatchFilterPattern(expr, testNodeFragment, properties));
				}
				case FilterOperator.Not:
				{
					IReadOnlyCollection<FilterExpression> subExpressions = operatorExpression.SubExpressions;
					IReadOnlyCollection<FilterExpression> source = subExpressions;
					return !MatchFilterPattern(source.Single(), testNodeFragment, properties);
				}
				}
			}
			else
			{
				if (filterExpression is ValueAndPropertyExpression valueAndPropertyExpression)
				{
					FilterExpression value = valueAndPropertyExpression.Value;
					FilterExpression properties2 = valueAndPropertyExpression.Properties;
					return MatchFilterPattern(value, testNodeFragment, properties) && MatchProperties(properties2, properties);
				}
				if (filterExpression is NopExpression)
				{
					return true;
				}
			}
			throw ApplicationStateGuard.Unreachable("/_/src/Platform/Microsoft.Testing.Platform/Requests/TreeNodeFilter/TreeNodeFilter.cs", 558);
		}
		return valueExpression.Regex.IsMatch(testNodeFragment);
	}

	private static bool MatchProperties(FilterExpression propertyExpr, PropertyBag properties)
	{
		if (propertyExpr is PropertyExpression propertyExpression)
		{
			ValueExpression propertyName = propertyExpression.PropertyName;
			ValueExpression value = propertyExpression.Value;
			return properties.AsEnumerable().Any((IProperty prop) => IsMatchingProperty(prop, propertyName, value));
		}
		if (propertyExpr is OperatorExpression operatorExpression)
		{
			switch (operatorExpression.Op)
			{
			case FilterOperator.Or:
			{
				IReadOnlyCollection<FilterExpression> subExpressions = operatorExpression.SubExpressions;
				return subExpressions.Any((FilterExpression expr) => MatchProperties(expr, properties));
			}
			case FilterOperator.And:
			{
				IReadOnlyCollection<FilterExpression> subExpressions = operatorExpression.SubExpressions;
				IReadOnlyCollection<FilterExpression> source2 = subExpressions;
				return source2.All((FilterExpression expr) => MatchProperties(expr, properties));
			}
			case FilterOperator.Not:
			{
				IReadOnlyCollection<FilterExpression> subExpressions = operatorExpression.SubExpressions;
				IReadOnlyCollection<FilterExpression> source = subExpressions;
				return !MatchProperties(source.Single(), properties);
			}
			}
		}
		throw ApplicationStateGuard.Unreachable("/_/src/Platform/Microsoft.Testing.Platform/Requests/TreeNodeFilter/TreeNodeFilter.cs", 574);
	}

	private static bool IsMatchingProperty(IProperty prop, ValueExpression propExpr, ValueExpression valueExpr)
	{
		if (prop is TestMetadataProperty testMetadataProperty && propExpr.Regex.IsMatch(testMetadataProperty.Key))
		{
			return valueExpr.Regex.IsMatch(testMetadataProperty.Value);
		}
		return false;
	}
}
