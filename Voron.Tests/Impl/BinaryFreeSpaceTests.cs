﻿// -----------------------------------------------------------------------
//  <copyright file="FreePagesRepositoryTests.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using Voron.Impl;
using Xunit;

namespace Voron.Tests.Impl
{
	public class FreeSpaceBuffersTests : IDisposable
	{
		[Fact]
		public void InitiallyAllTheBitsAreCleared()
		{
			using (var freeSpace = new FreeSpaceBuffers("free-space", 8))
			{
				for (int i = 0; i < freeSpace.NumberOfTrackedPages; i++)
				{
					Assert.False(freeSpace.Buffers[0].AllBits[i]);
					Assert.False(freeSpace.Buffers[1].AllBits[i]);
				}
			}
		}

		[Fact]
		public void AddingFreePagesWillSetBitsAtCorrectPositions()
		{
			using (var freeSpace = new FreeSpaceBuffers("free-space", 4))
			{
				const int transactionNumber = 1;

				var buffer = freeSpace.GetBufferForTransaction(transactionNumber);

				buffer.FreePages.Set(1);
				buffer.FreePages.Set(3);

				Assert.Equal(buffer.FreePages[0], false);
				Assert.Equal(buffer.FreePages[1], true);
				Assert.Equal(buffer.FreePages[2], false);
				Assert.Equal(buffer.FreePages[3], true);
			}
		}

		[Fact]
		public void ConsecutiveTransactionsShouldUseBuffersAlternately()
		{
			using (var freeSpace = new FreeSpaceBuffers("free-space", 4))
			{
				var buffer1 = freeSpace.GetBufferForTransaction(0);
				var buffer2 = freeSpace.GetBufferForTransaction(1);
				var buffer3 = freeSpace.GetBufferForTransaction(2);
				var buffer4 = freeSpace.GetBufferForTransaction(3);

				buffer1.FreePages.Set(0);
				buffer2.FreePages.Set(1);
				buffer3.FreePages.Set(2);
				buffer4.FreePages.Set(3); 

				Assert.Equal(freeSpace.Buffers[0].FreePages[0], true);
				Assert.Equal(freeSpace.Buffers[0].FreePages[1], false);
				Assert.Equal(freeSpace.Buffers[0].FreePages[2], true);
				Assert.Equal(freeSpace.Buffers[0].FreePages[3], false);

				Assert.Equal(freeSpace.Buffers[1].FreePages[0], false);
				Assert.Equal(freeSpace.Buffers[1].FreePages[1], true);
				Assert.Equal(freeSpace.Buffers[1].FreePages[2], false);
				Assert.Equal(freeSpace.Buffers[1].FreePages[3], true);
			}
		}

		[Fact]
		public void ShouldNotFindAnyFreePagesWhenThereAreNone()
		{
			using (var freeSpace = new FreeSpaceBuffers("free-space", 4))
			{
				var numbersOfFreePages = freeSpace.GetBufferForTransaction(1).Find(2);

				Assert.Null(numbersOfFreePages);
			}
		}

		[Fact]
		public void CanGetFreePagesFromTheBegin()
		{
			using (var freeSpace = new FreeSpaceBuffers("free-space", 4))
			{
				const int transaction = 1;

				var buffer = freeSpace.GetBufferForTransaction(transaction);

				buffer.FreePages.Set(0);
				buffer.FreePages.Set(1);

				var numbersOfFreePages = buffer.Find(2);

				Assert.Equal(2, numbersOfFreePages.Count);
				Assert.Equal(0, numbersOfFreePages[0]);
				Assert.Equal(1, numbersOfFreePages[1]);
			}
		}

		[Fact]
		public void CanGetFreePagesFromTheEnd()
		{
			using (var freeSpace = new FreeSpaceBuffers("free-space", 4))
			{
				const int transaction = 1;

				var buffer = freeSpace.GetBufferForTransaction(transaction);

				buffer.FreePages.Set(2);
				buffer.FreePages.Set(3);

				var numbersOfFreePages = buffer.Find(2);

				Assert.Equal(2, numbersOfFreePages.Count);
				Assert.Equal(2, numbersOfFreePages[0]);
				Assert.Equal(3, numbersOfFreePages[1]);
			}
		}

		[Fact]
		public void CanGetFreePagesFromTheMiddleEnd()
		{
			using (var freeSpace = new FreeSpaceBuffers("free-space", 4))
			{
				const int transaction = 1;

				var buffer = freeSpace.GetBufferForTransaction(transaction);

				buffer.FreePages.Set(1);
				buffer.FreePages.Set(2);

				var numbersOfFreePages = buffer.Find(2);

				Assert.Equal(2, numbersOfFreePages.Count);
				Assert.Equal(1, numbersOfFreePages[0]);
				Assert.Equal(2, numbersOfFreePages[1]);
			}
		}

		[Fact]
		public void GettingFreePagesShouldMarkThemAsBusy()
		{
			using (var freePages = new FreeSpaceBuffers("free-space", 10))
			{
				const int transaction = 1;

				var buffer = freePages.GetBufferForTransaction(transaction);

				buffer.FreePages.Set(4);
				buffer.FreePages.Set(5);
				buffer.FreePages.Set(6);

				buffer.Find(2);

				var pages = buffer.FreePages;

				Assert.False(pages[4]); // should mark as busy
				Assert.False(pages[5]); // should mark as busy
				Assert.True(pages[6]); // should remain free
			}
		}

		[Fact]
		public void WillGetFirstRangeWithEnoughFreePages()
		{
			using (var freePages = new FreeSpaceBuffers("free-space", 10))
			{
				const int transaction = 1;

				var buffer = freePages.GetBufferForTransaction(transaction);

				buffer.FreePages.Set(5);
				buffer.FreePages.Set(1);

				buffer.FreePages.Set(4);
				buffer.FreePages.Set(5);

				buffer.FreePages.Set(7);
				buffer.FreePages.Set(8);
				buffer.FreePages.Set(9);

				var numbersOfFreePages = buffer.Find(2);

				Assert.Equal(2, numbersOfFreePages.Count);
				Assert.Equal(4, numbersOfFreePages[0]);
				Assert.Equal(5, numbersOfFreePages[1]);

				numbersOfFreePages = buffer.Find(1);

				Assert.Equal(1, numbersOfFreePages.Count);
				Assert.Equal(7, numbersOfFreePages[0]);

				numbersOfFreePages = buffer.Find(2);

				Assert.Equal(2, numbersOfFreePages.Count);
				Assert.Equal(8, numbersOfFreePages[0]);
				Assert.Equal(9, numbersOfFreePages[1]);
			}
		}

		[Fact]
		public void MustNotReturnLessFreePagesThanRequested()
		{
			using (var freePages = new FreeSpaceBuffers("free-space", 5))
			{
				const int transaction = 1;

				var buffer = freePages.GetBufferForTransaction(transaction);

				buffer.FreePages.Set(2);
				buffer.FreePages.Set(3);

				// 2 pages are free but we request for 3
				var numbersOfFreePages = buffer.Find(3);

				Assert.Null(numbersOfFreePages);
			}
		}

		[Fact]
		public void MustNotMergeFreePagesFromEndAndBegin()
		{
			using (var freePages = new FreeSpaceBuffers("free-space", 10))
			{
				const int transaction = 1;

				var buffer = freePages.GetBufferForTransaction(transaction);

				buffer.FreePages.Set(7);
				buffer.FreePages.Set(8);
				buffer.FreePages.Set(9);

				var numbersOfFreePages = buffer.Find(1); // move searching index

				Assert.Equal(1, numbersOfFreePages.Count);
				Assert.Equal(7, numbersOfFreePages[0]);

				buffer.FreePages.Set(0); // free page 0

				numbersOfFreePages = buffer.Find(3);
				Assert.Null(numbersOfFreePages);
			}
		}

		[Fact]
		public void WhenBufferIsDirtyShouldCopySecondBufferBeforeCanProcess()
		{
			using (var freePages = new FreeSpaceBuffers("free-space", 10))
			{
				var tx1 = 1;
				var tx2 = 2;

				var buffer1 = freePages.GetBufferForTransaction(tx1);
				buffer1.FreePages.Set(1);
				buffer1.FreePages.Set(3);

				var buffer2 = freePages.GetBufferForTransaction(tx2);
				buffer2.FreePages.Set(4);
				buffer2.FreePages.Set(7);
				buffer2.FreePages.Set(9);

				buffer1.IsDirty = true; // force as dirty so next get should return a clean buffer which will be a copy of a second buffer

				buffer1 = freePages.GetBufferForTransaction(tx1);

				Assert.False(buffer1.IsDirty);
				Assert.Equal(buffer1.AllBits.Size, buffer2.AllBits.Size);

				for (int i = 0; i < buffer1.FreePages.Size; i++)
				{
					Assert.Equal(buffer1.FreePages[i], buffer2.FreePages[i]);
				}
			}
		}

		[Fact]
		public void WhenBufferIsCleanShouldCopyModifiedPagesFromSecondBufferBeforeCanProcess()
		{
			using (var freePages = new FreeSpaceBuffers("free-space", 10))
			{
				var tx1 = 1;
				var tx2 = 2;

				var buffer2 = freePages.GetBufferForTransaction(tx2);

				buffer2.ModifiedPages.Set(0);
				buffer2.ModifiedPages.Set(3);
				buffer2.ModifiedPages.Set(9);

				var buffer1 = freePages.GetBufferForTransaction(tx1);

				Assert.True(buffer1.FreePages[0]);
				Assert.True(buffer1.FreePages[3]);
				Assert.True(buffer1.FreePages[9]);
			}
		}

		[Fact]
		public void ReturnedBufferShouldAlwaysHaveCleanModifiedPages()
		{
			using (var freePages = new FreeSpaceBuffers("free-space", 10))
			{
				var tx1 = 1;

				var buffer = freePages.GetBufferForTransaction(tx1);

				buffer.ModifiedPages.Set(0);
				buffer.ModifiedPages.Set(3);
				buffer.ModifiedPages.Set(9);

				buffer = freePages.GetBufferForTransaction(tx1); // should clean modified pages

				for (int i = 0; i < buffer.ModifiedPages.Size; i++)
				{
					Assert.False(buffer.ModifiedPages[i]);
				}
			}
		}

		[Fact]
		public void CanSetPageAsModified()
		{
			using (var freePages = new FreeSpaceBuffers("free-space", 10))
			{
				var tx = 1;

				var buffer = freePages.GetBufferForTransaction(tx);

				buffer.ModifiedPages.Set(1);
				buffer.ModifiedPages.Set(5);
				buffer.ModifiedPages.Set(6);

				Assert.True(buffer.ModifiedPages[1]);
				Assert.True(buffer.ModifiedPages[5]);
				Assert.True(buffer.ModifiedPages[6]);
			}
		}

		private void DeleteFiles()
		{
			if (File.Exists("free-space-0"))
				File.Delete("free-space-0");

			if (File.Exists("free-space-1"))
				File.Delete("free-space-1");
		}

		public void Dispose()
		{
			DeleteFiles();
		}
	}
}