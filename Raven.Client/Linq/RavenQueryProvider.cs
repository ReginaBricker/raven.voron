﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Raven.Database.Data;

namespace Raven.Client.Linq
{
	public class RavenQueryProvider<T> : IRavenQueryProvider
    {
		private enum SpecialQueryType
		{
			None,
			All,
			Any,
			Count,
			First,
			FirstOrDefault,
			Single,
			SingleOrDefault
		}

        private readonly IDocumentSession session;
        private readonly string indexName;

        private Action<IDocumentQuery<T>> customizeQuery;
    	private IDocumentQuery<T> luceneQuery;

    	public IDocumentSession Session
        {
            get { return session; }
        }

        public string IndexName
        {
            get { return indexName; }
        }

		public IDocumentQuery<T> LuceneQuery
		{
			get { return this.luceneQuery; }
		}

    	public QueryResult QueryResult
    	{
    		get
    		{
				if (luceneQuery == null)
					return null;
    			return luceneQuery.QueryResult;
    		}
    	}

    	public RavenQueryProvider(IDocumentSession session, string indexName)
        {
            this.session = session;
            this.indexName = indexName;
            FieldsToFetch = new List<string>();
        }

        public List<string> FieldsToFetch { get; set; }

        private int? skipValue;
        private int? takeValue;

        private SpecialQueryType queryType = SpecialQueryType.None;

		private Expression<Func<T, bool>> predicate;

        public object Execute(Expression expression)
        {
        	luceneQuery = session.LuceneQuery<T>(indexName);
			ProcessExpression(expression);

            if (skipValue.HasValue)
            {
				luceneQuery.Skip(skipValue.Value);
            }
            if (takeValue.HasValue)
            {
				luceneQuery.Take(takeValue.Value);
            }

			luceneQuery.SelectFields<T>(FieldsToFetch.ToArray());            

			if (customizeQuery != null)
				customizeQuery(luceneQuery);

			switch (queryType)
			{
				case SpecialQueryType.First:
				{
					return luceneQuery.First();               
				}
				case SpecialQueryType.FirstOrDefault:
				{
					return luceneQuery.FirstOrDefault();
				}
				case SpecialQueryType.Single:
				{
					return luceneQuery.Single();
				}
				case SpecialQueryType.SingleOrDefault:
				{
					return luceneQuery.SingleOrDefault();
				}
				case SpecialQueryType.All:
				{
					return luceneQuery.AsQueryable().All(this.predicate);
				}
				case SpecialQueryType.Any:
				{
					return luceneQuery.Any();
				}
				case SpecialQueryType.Count:
				{
					return luceneQuery.QueryResult.TotalResults;
				}
				default:
				{
					return luceneQuery;
				}
			}
        }

        public void ProcessExpression(Expression expression)
        {
            VisitExpression(expression);
        }

        private void VisitExpression(Expression expression)
        {
            switch (expression.NodeType)
            {
                case ExpressionType.OrElse:
                    VisitOrElse((BinaryExpression)expression);
                    break;
                case ExpressionType.AndAlso:
                    VisitAndAlso((BinaryExpression)expression);
                    break;
                case ExpressionType.Equal:
                    VisitEqual((BinaryExpression)expression);
                    break;
                case ExpressionType.GreaterThan:
                    VisitGreaterThan((BinaryExpression)expression);
                    break;
                case ExpressionType.GreaterThanOrEqual:
                    VisitGreaterThanOrEqual((BinaryExpression)expression);
                    break;
                case ExpressionType.LessThan:
                    VisitLessThan((BinaryExpression)expression);
                    break;
                case ExpressionType.LessThanOrEqual:
                    VisitLessThanOrEqual((BinaryExpression)expression);
                    break;
                case ExpressionType.MemberAccess:
                    VisitMemberAccess((MemberExpression)expression, true);
                    break;
                case ExpressionType.Not:
                    var unaryExpressionOp = ((UnaryExpression)expression).Operand;                       
                    VisitMemberAccess((MemberExpression)unaryExpressionOp, false);                    
                    break;
                default:
                    if (expression is MethodCallExpression)
                    {
                        VisitMethodCall((MethodCallExpression)expression);
                    }
                    else if (expression is LambdaExpression)
                    {
                        VisitExpression(((LambdaExpression)expression).Body);
                    }
                    break;
            }
        }
       
        private void VisitAndAlso(BinaryExpression andAlso)
        {
            VisitExpression(andAlso.Left);

			luceneQuery.AndAlso();

            VisitExpression(andAlso.Right);
        }

        private void VisitOrElse(BinaryExpression orElse)
        {
            VisitExpression(orElse.Left);

			luceneQuery.OrElse();

            VisitExpression(orElse.Right);
        }

        private void VisitEqual(BinaryExpression expression)
        {
			luceneQuery.WhereEqual(
				((MemberExpression)expression.Left).Member.Name,
				GetValueFromExpression(expression.Right));
        }

		private void VisitContains(MethodCallExpression expression)
		{
			luceneQuery.WhereContains(
				GetFieldName(expression.Object),
				GetValueFromExpression(expression.Arguments[0]));
		}

		private void VisitGreaterThan(BinaryExpression expression)
		{
			object value = GetValueFromExpression(expression.Right);

			luceneQuery.WhereGreaterThan(
				GetFieldNameForRangeQuery(expression.Left, value),
				value);
		}

		private void VisitGreaterThanOrEqual(BinaryExpression expression)
		{
			object value = GetValueFromExpression(expression.Right);

			luceneQuery.WhereGreaterThanOrEqual(
				GetFieldNameForRangeQuery(expression.Left, value),
				value);
		}

		private void VisitLessThan(BinaryExpression expression)
		{
			object value = GetValueFromExpression(expression.Right);

			luceneQuery.WhereLessThan(
				GetFieldNameForRangeQuery(expression.Left, value),
				value);
		}

        private void VisitLessThanOrEqual(BinaryExpression expression)
        {
			object value = GetValueFromExpression(expression.Right);

			luceneQuery.WhereLessThanOrEqual(
				GetFieldNameForRangeQuery(expression.Left, value),
				value);
        }

        private void VisitMemberAccess(MemberExpression memberExpression, bool boolValue)
        {            
            if (memberExpression.Type == typeof(bool))
            {
				luceneQuery.WhereEqual(
					memberExpression.Member.Name,
					boolValue);
            }
            else
            {
                throw new NotSupportedException("Expression type not supported: " + memberExpression.ToString());
            }
        }

    	private void VisitMethodCall(MethodCallExpression expression)
        {
			if (expression.Method.DeclaringType != typeof(Queryable))
			{
				if (expression.Method.DeclaringType == typeof(String) &&
					expression.Method.Name == "Contains")
				{
					VisitContains(expression);
					return;
				}

				throw new NotSupportedException("Method not supported: " + expression.Method.Name);
			}

			switch (expression.Method.Name)
			{
				case "Where":
				{
					VisitExpression(((UnaryExpression)expression.Arguments[1]).Operand);
					return;
				}
				case "Select":
				{
					VisitExpression(expression.Arguments[0]);
					VisitSelect(((UnaryExpression)expression.Arguments[1]).Operand);
					return;
				}
				case "Skip":
				{
					VisitExpression(expression.Arguments[0]);
					VisitSkip(((ConstantExpression)expression.Arguments[1]));
					return;
				}
				case "Take":
				{
					VisitExpression(expression.Arguments[0]);
					VisitTake(((ConstantExpression)expression.Arguments[1]));
					return;
				}
				case "First":
				case "FirstOrDefault":
				{
					VisitExpression(expression.Arguments[0]);
					if (expression.Arguments.Count == 2)
					{
						VisitExpression(((UnaryExpression)expression.Arguments[1]).Operand);
					}

					if (expression.Method.Name == "First")
					{
						VisitFirst();
					}
					else
					{
						VisitFirstOrDefault();
					}
					return;
				}
				case "Single":
				case "SingleOrDefault":
				{
					VisitExpression(expression.Arguments[0]);
					if (expression.Arguments.Count == 2)
					{
						VisitExpression(((UnaryExpression)expression.Arguments[1]).Operand);
					}

					if (expression.Method.Name == "Single")
					{
						VisitSingle();
					}
					else
					{
						VisitSingleOrDefault();
					}
					return;
				}
				case "All":
				{
					VisitExpression(expression.Arguments[0]);
					VisitAll((Expression<Func<T, bool>>)((UnaryExpression)expression.Arguments[1]).Operand);
					return;
				}
				case "Any":
				{
					VisitExpression(expression.Arguments[0]);
					if (expression.Arguments.Count == 2)
					{
						VisitExpression(((UnaryExpression)expression.Arguments[1]).Operand);
					}

					VisitAny();
					return;
				}
				case "Count":
				{
					VisitExpression(expression.Arguments[0]);
					if (expression.Arguments.Count == 2)
					{
						VisitExpression(((UnaryExpression)expression.Arguments[1]).Operand);
					}

					VisitCount();
					return;
				}
				default:
				{
					throw new NotSupportedException("Method not supported: " + expression.Method.Name);
				}
			}
        }

        private void VisitSelect(Expression operand)
        {
            var body = ((LambdaExpression)operand).Body;
            switch (body.NodeType)
            {
                case ExpressionType.MemberAccess:
                    FieldsToFetch.Add(((MemberExpression)body).Member.Name);
                    break;
                case ExpressionType.New:
                    FieldsToFetch.AddRange(((NewExpression)body).Arguments.Cast<MemberExpression>().Select(x => x.Member.Name));
                    break;
				case ExpressionType.Parameter:// want the full thing, so just pass it on.
            		break;
                default:
                    throw new NotSupportedException("Node not supported: " + body.NodeType);

            }
        }

        private void VisitSkip(ConstantExpression constantExpression)
        {
            //Don't have to worry about the cast failing, the Skip() extension method only takes an int
            skipValue = (int)constantExpression.Value;
        }

        private void VisitTake(ConstantExpression constantExpression)
        {
            //Don't have to worry about the cast failing, the Take() extension method only takes an int
            takeValue = (int)constantExpression.Value;
        }

		private void VisitAll(Expression<Func<T,bool>> predicateExpression)
		{
			this.predicate = predicateExpression;
			queryType = SpecialQueryType.All;
		}

		private void VisitAny()
		{
			takeValue = 1;
			queryType = SpecialQueryType.Any;
		}

		private void VisitCount()
		{
			takeValue = 1;
			queryType = SpecialQueryType.Count;
		}

        private void VisitSingle()
        {
			takeValue = 2;           
            queryType = SpecialQueryType.Single;
        }
        
        private void VisitSingleOrDefault()
        {
			takeValue = 2;
            queryType = SpecialQueryType.SingleOrDefault;
        }

        private void VisitFirst()
        {
			takeValue = 1;
            queryType = SpecialQueryType.First;
        }

        private void VisitFirstOrDefault()
        {
			takeValue = 1;
            queryType = SpecialQueryType.FirstOrDefault;
        }        
		
        IQueryable<S> IQueryProvider.CreateQuery<S>(Expression expression)
        {
            return new RavenQueryable<S>(this, expression);
        }

        IQueryable IQueryProvider.CreateQuery(Expression expression)
        {
            Type elementType = TypeSystem.GetElementType(expression.Type);
            try
            {
                return
                    (IQueryable)
                    Activator.CreateInstance(typeof(RavenQueryable<>).MakeGenericType(elementType),
                                             new object[] { this, expression });
            }
            catch (TargetInvocationException tie)
            {
                throw tie.InnerException;
            }
        }

        S IQueryProvider.Execute<S>(Expression expression)
        {
            return (S)Execute(expression);
        }

        object IQueryProvider.Execute(Expression expression)
        {
            return Execute(expression);
        }

        public void Customize(Delegate action)
        {
            customizeQuery = (Action<IDocumentQuery<T>>)action;
        }

        #region Helpers

		private static string GetFieldNameForRangeQuery(Expression expression, object value)
		{
			if (value is int || value is long || value is double || value is float || value is decimal)
				return ((MemberExpression)expression).Member.Name + "_Range";
			return ((MemberExpression)expression).Member.Name;
		}

		private string GetFieldName(Expression expression)
		{
			MemberExpression member = expression as MemberExpression;

			if (expression == null)
			{
				throw new NotSupportedException("Unable to determine field name from expression");
			}

			return member.Member.Name;
		}

        private static object GetValueFromExpression(Expression expression)
        {
            if (expression == null)
                throw new ArgumentNullException("expression");

            // Get object
            if (expression.NodeType == ExpressionType.Constant)
                return ((ConstantExpression)expression).Value;
			if(expression.NodeType == ExpressionType.MemberAccess)
				return GetMemberValue(((MemberExpression)expression));
			if(expression.NodeType == ExpressionType.New)
			{
				var newExpression = ((NewExpression)expression);
				return Activator.CreateInstance(newExpression.Type, newExpression.Arguments.Select(GetValueFromExpression).ToArray());
			}
			if (expression.NodeType == ExpressionType.Lambda)
				return ((LambdaExpression) expression).Compile().DynamicInvoke();
            if (expression.NodeType == ExpressionType.Call)
                return Expression.Lambda(expression).Compile().DynamicInvoke();
            if (expression.NodeType == ExpressionType.Convert)
                return Expression.Lambda(((UnaryExpression)expression).Operand).Compile().DynamicInvoke();
            throw new InvalidOperationException("Can't extract value from expression of type: " + expression.NodeType);
        }

		private static object GetMemberValue(MemberExpression memberExpression)
		{
			object obj;

			if (memberExpression == null)
				throw new ArgumentNullException("memberExpression");

			// Get object
			if (memberExpression.Expression is ConstantExpression)
				obj = ((ConstantExpression)memberExpression.Expression).Value;
			else if (memberExpression.Expression is MemberExpression)
				obj = GetMemberValue((MemberExpression)memberExpression.Expression);
			else
				throw new NotSupportedException("Expression type not supported: " + memberExpression.Expression.GetType().FullName);

			// Get value
			var memberInfo = memberExpression.Member;
			if (memberInfo is PropertyInfo)
			{
				var property = (PropertyInfo)memberInfo;
				return property.GetValue(obj, null);
			}
			if (memberInfo is FieldInfo)
			{
				var value = Expression.Lambda(memberExpression).Compile().DynamicInvoke();
				return value;
			}
			throw new NotSupportedException("MemberInfo type not supported: " + memberInfo.GetType().FullName);
		}

        #endregion Helpers
	}
}