using System.Collections.Generic;
using System.Linq;
using Raven.Client;
using Raven.Client.Indexes;
using Xunit;

namespace Raven.Tests.Bugs
{
    public class CanPageOverHierarchicalQuery : RavenTest
    {
        // Paging through tampered results: http://ravendb.net/docs/2.0/client-api/querying/paging
        [Fact]
        public void PagesCorrectlyUsingSkippedResults()
        {
            using (var store = NewDocumentStore())
            {
                //Based on: http://ravendb.net/docs/2.0/client-api/querying/static-indexes/indexing-hierarchies?version=2.0
                store.DatabaseCommands.PutIndex("booksByCategoryAndAuthorName",
                                                new IndexDefinitionBuilder<Book>
                                                {
                                                    Map = books => from book in books
                                                                   from author in book.Authors
                                                                   select new{
                                                                           AuthorName = author.Name,
                                                                           book.Categories
                                                                       }
                                                }.ToIndexDefinition(store.Conventions));

                SaveBooks(store);
                
                using (var session = store.OpenSession())
                {
                    const int pageSize = 3;

                    RavenQueryStatistics stats;
                    var page1 = session.Query<QueryResult>("booksByCategoryAndAuthorName")
                        .Customize(t=>t.WaitForNonStaleResults())
                        .Statistics(out stats)
                        .Search(result => result.Categories, "Fantasy")
                        .Skip(0 * pageSize)
                        .Take(pageSize)
                        .OfType<Book>()
                        .ToList();

                    var skippedResults = stats.SkippedResults;

                    var page2 = session.Query<QueryResult>("booksByCategoryAndAuthorName")
                        .Customize(t => t.WaitForNonStaleResults())
                        .Statistics(out stats)
                        .Search(result => result.Categories, "Fantasy")
                        .Skip((1 * pageSize) + skippedResults)
                        .Take(pageSize)
                        .OfType<Book>()
                        .ToList();

                    // Expecting 4 fantasy books spread over 2 pages. ( It works if we ignore SkippedResults
                    Assert.Equal(4, page1.Count() + page2.Count());
                }                                
            }
        }

        [Fact]
        public void TotalsShouldBalanceOnLastPage()
        {
            using (var store = NewRemoteDocumentStore(fiddler: true))
            {
                store.DatabaseCommands.PutIndex("booksByCategoryAndAuthorName",
                                                new IndexDefinitionBuilder<Book>
                                                {
                                                    Map = books => from book in books
                                                                   from author in book.Authors
                                                                   select new
                                                                   {
                                                                       author.Name,
                                                                       book.Categories
                                                                   }
                                                }.ToIndexDefinition(store.Conventions));

                SaveBooks(store);

                using (var session = store.OpenSession())
                {
                    const int start = 3;
                    RavenQueryStatistics stats;
                    var lastPage = session.Query<QueryResult>("booksByCategoryAndAuthorName")
                        .Customize(t => t.WaitForNonStaleResults())
                        .Statistics(out stats)
                        .Skip(start)
                        .Search(result => result.Categories, "Fantasy")
                        .OfType<Book>()
                        .ToList();

                    Assert.Equal(lastPage.Count + stats.SkippedResults + start, stats.TotalResults);
                }
            }
        }

        private static void SaveBooks(IDocumentStore store)
        {
            using (var session = store.OpenSession())
            {
                session.Store(new Book
                              {
                                  Title = "My Favourite Dragon",
                                  Categories = new[] {"Fantasy"},
                                  Authors = new List<Author> {new Author {Name = "George Martin"}}
                              });

                session.Store(new Book
                              {
                                  Title = "Space Adventure",
                                  Categories = new[] {"SciFi"},
                                  Authors = new List<Author> {new Author {Name = "George Martin"}}
                              });

                session.Store(new Book
                              {
                                  Title = "Dwarves In Space",
                                  Categories = new[] {"SciFi", "Fantasy"},
                                  Authors = new List<Author>
                                            {
                                                new Author {Name = "George Martin"},
                                                new Author {Name = "Brandon Sanderson"}
                                            }
                              });

                session.Store(new Book
                              {
                                  Title = "Boy Learns Magic",
                                  Categories = new[] {"Fantasy"},
                                  Authors = new List<Author>
                                            {
                                                new Author {Name = "George Martin"},
                                                new Author {Name = "Brandon Sanderson"}
                                            }
                              });

                session.Store(new Book
                              {
                                  Title = "School Of Dragons",
                                  Categories = new[] {"Fantasy"},
                                  Authors = new List<Author> {new Author {Name = "George Martin"}}
                              });

                session.SaveChanges();
            }
        }

        public class Author
        {
            public string Name { get; set; }         
        }
    
        public class Book
        {
            public string[] Categories { get; set; }
            public string Title { get; set; }
            public IList<Author> Authors { get; set; }
        }

        public class QueryResult
        {
            public string AuthorName { get; set; }
            public string[] Categories { get; set; }
        }
    }
}