﻿/* 
 * Copyright (c) 2014, Furore (info@furore.com) and contributors
 * See the file CONTRIBUTORS for details.
 * 
 * This file is licensed under the BSD 3-Clause license
 * available at https://raw.github.com/furore-fhir/spark/master/LICENSE
 */

using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spark.Core;

namespace Spark.Mongo
{

    public class MongoTransaction
    {
       

        string transid = null;

        MongoCollection<BsonDocument> collection;

        public MongoTransaction(MongoCollection<BsonDocument> collection)
        {
            this.collection = collection;
        }

        private void MarkExisting(BsonDocument document)
        {
            BsonValue id = document.GetValue(Field.RESOURCEID);
            IMongoQuery query = Query.And(Query.EQ(Field.RESOURCEID, id), Query.EQ(Field.STATE, Value.CURRENT));
            IMongoUpdate update = new UpdateDocument("$set",
                new BsonDocument
                { 
                    { Field.TRANSACTION, transid },
                }
            );
            collection.Update(query, update, UpdateFlags.Multi);
        }

        public IEnumerable<BsonValue> KeysOf(IEnumerable<BsonDocument> documents)
        {
            foreach(BsonDocument document in documents)
            {
                BsonValue value = null;
                if (document.TryGetValue(Field.RESOURCEID, out value))
                {
                    yield return value;
                }
            }
        }

        public BsonValue KeyOf(BsonDocument document)
        {
            BsonValue value = null;
            if (document.TryGetValue(Field.RESOURCEID, out value))
            {
                return value;
            }
            else
            {
                return null;
            }
        }


        public void MarkExisting(IEnumerable<BsonDocument> documents)
        {
            IEnumerable<BsonValue> keys = KeysOf(documents);
            IMongoUpdate update = new UpdateDocument("$set",
                new BsonDocument
                { 
                    { Field.TRANSACTION, this.transid },
                }
            );
            IMongoQuery query = Query.And(Query.EQ(Field.STATE, Value.CURRENT),  Query.In(Field.RESOURCEID, keys));
            collection.Update(query, update, UpdateFlags.Multi);
        }

        public void RemoveQueued(string transid)
        {
            IMongoQuery query = Query.And(
                Query.EQ(Field.TRANSACTION, transid),
                Query.EQ(Field.STATE, Value.QUEUED)
                );
            collection.Remove(query);
        }

        public void RemoveTransaction(string transid)
        {
            IMongoQuery query = Query.EQ(Field.TRANSACTION, transid);
            IMongoUpdate update = new UpdateDocument("$set",
                new BsonDocument
                {
                    { Field.TRANSACTION, 0 }
                }
            );
            collection.Update(query, update, UpdateFlags.Multi);
        }
        
        private void prepareNew(BsonDocument document)
        {
            //document.Remove(Field.RecordId); voor Fhir-documenten niet nodig
            document.Set(Field.TRANSACTION, transid);
            document.Set(Field.STATE, Value.QUEUED);
        }

        private void PrepareNew(IEnumerable<BsonDocument> documents)
        {
            foreach(BsonDocument doc in documents)
            {
                prepareNew(doc);
            }
        }
        
        private void sweep(string transid, string statusfrom, string statusto)
        {
            IMongoQuery query = Query.And(Query.EQ(Field.TRANSACTION, transid), Query.EQ(Field.STATE, statusfrom));
            IMongoUpdate update = new UpdateDocument("$set",
                new BsonDocument
                {
                    { Field.STATE, statusto }
                }
            );
            collection.Update(query, update, UpdateFlags.Multi);
            
        }

        public void Begin()
        {
            transid = Guid.NewGuid().ToString();
        }

        public void Rollback()
        {
            RemoveQueued(this.transid);
            RemoveTransaction(this.transid);
        }

        public void Commit()
        {
            sweep(transid, Value.CURRENT, Value.SUPERCEDED);
            sweep(transid, Value.QUEUED, Value.CURRENT);
        }
        
        public void Insert(BsonDocument document)
        {
            MarkExisting(document);
            prepareNew(document);
            collection.Save(document);
        }

        public void InsertBatch(List<BsonDocument> documents)
        {
            MarkExisting(documents);
            PrepareNew(documents);
            collection.InsertBatch(documents);
        }

        public void Update(BsonDocument doc)
        {
            MarkExisting(doc);
            prepareNew(doc);
            collection.Save(doc);
        }

        public void Delete(BsonDocument doc)
        {
            MarkExisting(doc);
            prepareNew(doc);
            collection.Save(doc);
        }

        public BsonDocument ReadCurrent(string resourceid)
        {
            IMongoQuery query = 
                Query.And(
                    Query.EQ(Field.RESOURCEID, resourceid),
                    Query.EQ(Field.STATE, Value.CURRENT)
                );
            return collection.FindOne(query);
        }
        
    }
}