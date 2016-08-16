﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Orleans.Runtime;
using Orleans.Streams;
using OrleansAWSUtils.Storage;

namespace OrleansAWSUtils.Providers.Streams.PersistentStreams
{
    /// <summary>
    /// Delivery failure handler that writes failures to azure table storage.
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    public class DynamoDbStorageStreamFailureHandler<TEntity> : IStreamFailureHandler where TEntity : StreamDeliveryFailureEntity, new()
    {
        private static readonly Func<TEntity> DefaultCreateEntity = () => new TEntity();
        private readonly string deploymentId;
        private readonly string _tableName;
        private readonly DynamoDBStorage dataManager;
        private readonly Func<TEntity> createEntity;

        /// <summary>
        /// Delivery failure handler that writes failures to azure table storage.
        /// </summary>
        /// <param name="faultOnFailure"></param>
        /// <param name="deploymentId"></param>
        /// <param name="tableName"></param>
        /// <param name="storageConnectionString"></param>
        /// <param name="createEntity"></param>
        public DynamoDbStorageStreamFailureHandler(bool faultOnFailure, string deploymentId, string tableName, string storageConnectionString, Func<TEntity> createEntity = null)
        {
            if (string.IsNullOrEmpty(deploymentId))
            {
                throw new ArgumentNullException("deploymentId");
            }
            if (string.IsNullOrEmpty(tableName))
            {
                throw new ArgumentNullException("tableName");
            }
            if (string.IsNullOrEmpty(storageConnectionString))
            {
                throw new ArgumentNullException("storageConnectionString");
            }
            this.deploymentId = deploymentId;
            _tableName = tableName;
            ShouldFaultSubsriptionOnError = faultOnFailure;
            this.createEntity = createEntity ?? DefaultCreateEntity;
            dataManager = new DynamoDBStorage(storageConnectionString);
        }

        /// <summary>
        /// Indicates if the subscription should be put in a fauted state upon stream failures
        /// </summary>
        public bool ShouldFaultSubsriptionOnError { get; private set; }

        /// <summary>
        /// Initialization
        /// </summary>
        /// <returns></returns>
        public Task InitAsync()
        {
            return dataManager.InitializeTable(_tableName, new List<KeySchemaElement>
            {
                new KeySchemaElement("PartitionKey", KeyType.HASH),
                new KeySchemaElement("RowKey", KeyType.RANGE)
            }, 
            null);
        }

        /// <summary>
        /// Should be called when an event could not be delivered to a consumer, after exhausting retry attempts.
        /// </summary>
        /// <param name="subscriptionId"></param>
        /// <param name="streamProviderName"></param>
        /// <param name="streamIdentity"></param>
        /// <param name="sequenceToken"></param>
        /// <returns></returns>
        public Task OnDeliveryFailure(GuidId subscriptionId, string streamProviderName, IStreamIdentity streamIdentity,
            StreamSequenceToken sequenceToken)
        {
            return OnFailure(subscriptionId, streamProviderName, streamIdentity, sequenceToken);
        }

        /// <summary>
        /// Should be called when a subscription requested by a consumer could not be setup, after exhausting retry attempts.
        /// </summary>
        /// <param name="subscriptionId"></param>
        /// <param name="streamProviderName"></param>
        /// <param name="streamIdentity"></param>
        /// <param name="sequenceToken"></param>
        /// <returns></returns>
        public Task OnSubscriptionFailure(GuidId subscriptionId, string streamProviderName, IStreamIdentity streamIdentity,
            StreamSequenceToken sequenceToken)
        {
            return OnFailure(subscriptionId, streamProviderName, streamIdentity, sequenceToken);
        }

        private async Task OnFailure(GuidId subscriptionId, string streamProviderName, IStreamIdentity streamIdentity,
                StreamSequenceToken sequenceToken)
        {
            if (subscriptionId == null)
            {
                throw new ArgumentNullException("subscriptionId");
            }
            if (string.IsNullOrWhiteSpace(streamProviderName))
            {
                throw new ArgumentNullException("streamProviderName");
            }
            if (streamIdentity == null)
            {
                throw new ArgumentNullException("streamIdentity");
            }

            var failureEntity = createEntity();
            failureEntity.SubscriptionId = subscriptionId.Guid;
            failureEntity.StreamProviderName = streamProviderName;
            failureEntity.StreamGuid = streamIdentity.Guid;
            failureEntity.StreamNamespace = streamIdentity.Namespace;
            failureEntity.SetSequenceToken(sequenceToken);
            failureEntity.SetPartitionKey(deploymentId);
            failureEntity.SetRowkey();

            await dataManager.PutEntryAsync(_tableName, failureEntity.ToAttributeValueDictionary() );
        }
    }
}
