﻿// -----------------------------------------------------------------------
//  <copyright file="LogFile.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Voron.Impl.Paging;
using Voron.Trees;
using Voron.Util;

namespace Voron.Impl.Journal
{
    public unsafe class JournalFile : IDisposable
    {
        private readonly IJournalWriter _journalWriter;
        private long _writePage;
        private bool _disposed;
        private int _refs;
        private readonly PageTable _pageTranslationTable = new PageTable();
        private readonly object _locker = new object();

        public class PagePosition
        {
			public PageFromScratchBuffer ScratchPos;
            public long JournalPos;
            public long TransactionId;
        }

        public JournalFile(IJournalWriter journalWriter, long journalNumber)
        {
            Number = journalNumber;
            _journalWriter = journalWriter;
            _writePage = 0;
        }

        public override string ToString()
        {
            return string.Format("Number: {0}", Number);
        }

        public JournalFile(IJournalWriter journalWriter, long journalNumber, long lastSyncedPage)
            : this(journalWriter, journalNumber)
        {
            _writePage = lastSyncedPage + 1;
        }

#if DEBUG
        private readonly StackTrace _st = new StackTrace(true);
#endif

        ~JournalFile()
        {
            Dispose();

#if DEBUG
            Trace.WriteLine(
                "Disposing a journal file from finalizer! It should be diposed by using JournalFile.Release() instead!. Log file number: " +
                Number + ". Number of references: " + _refs + " " + _st);
#endif
        }

        internal long WritePagePosition
        {
            get { return _writePage; }
        }

        public long Number { get; private set; }


        public long AvailablePages
        {
            get { return _journalWriter.NumberOfAllocatedPages - _writePage; }
        }

        internal IJournalWriter JournalWriter
        {
            get { return _journalWriter; }
        }

        public PageTable PageTranslationTable
        {
            get { return _pageTranslationTable; }
        }

        public void Release()
        {
            if (Interlocked.Decrement(ref _refs) != 0)
                return;

            Dispose();
        }

        public void AddRef()
        {
            Interlocked.Increment(ref _refs);
        }

        public void Dispose()
        {
            DisposeWithoutClosingPager();
            _journalWriter.Dispose();
        }

        public JournalSnapshot GetSnapshot()
        {
            var lastTxId = _pageTranslationTable.GetLastSeenTransaction();
            return new JournalSnapshot
            {
                Number = Number,
                AvailablePages = AvailablePages,
                PageTranslationTable = _pageTranslationTable,
                LastTransaction = lastTxId
            };
        }

        public void DisposeWithoutClosingPager()
        {
            if (_disposed)
                return;

            GC.SuppressFinalize(this);

            _disposed = true;
        }

        public bool ReadTransaction(long pos, TransactionHeader* txHeader)
        {
            return _journalWriter.Read(pos, (byte*)txHeader, sizeof(TransactionHeader));
        }

        public void Write(Transaction tx, byte*[] pages)
        {
            var txPages = tx.GetWrittenTransactionPages();

            var ptt = new Dictionary<long, PagePosition>();
            var writePagePos = _writePage;

            UpdatePageTranslationTable(tx, txPages,ptt);

            lock (_locker)
            {
                _writePage += pages.Length;
                _pageTranslationTable.SetItems(tx, ptt);
            }

            _journalWriter.WriteGather(writePagePos * AbstractPager.PageSize, pages);
        }

	    private unsafe void UpdatePageTranslationTable(Transaction tx, List<PageFromScratchBuffer> txPages, Dictionary<long, PagePosition> ptt)
	    {
		    for (int index = 1; index < txPages.Count; index++)
		    {
			    var txPage = txPages[index];
			    var scratchPage = tx.Environment.ScratchBufferPool.ReadPage(txPage);
			    var pageNumber = ((PageHeader*)scratchPage.Base)->PageNumber;

			    ptt[pageNumber] = new PagePosition
			    {
				    ScratchPos = txPage,
				    JournalPos = -1, // needed only during recovery and calculated there
				    TransactionId = tx.Id
			    };
		    }
	    }


        public void InitFrom(JournalReader journalReader)
        {
            _writePage = journalReader.NextWritePage;
        }

        public bool DeleteOnClose { set { _journalWriter.DeleteOnClose = value; } }

        public void FreeScratchPagesOlderThan(StorageEnvironment env, long lastSyncedTransactionId)
        {
            List<PageFromScratchBuffer> unusedPages;

            lock (_locker)
            {
				unusedPages = _pageTranslationTable.RemovePagesOlderThan(lastSyncedTransactionId);
            }

            foreach (var unusedScratchPage in unusedPages)
            {
                env.ScratchBufferPool.Free(unusedScratchPage);
            }
        }
    }
}