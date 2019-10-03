using Dexih.Utils.ManagedTasks;
using MessagePack;
using Newtonsoft.Json;
using System.Text.Json;
using Xunit;

namespace Dexih.Utils.Managed.Tasks.Tests
{
    public class serlializeation
    {
        void Progress(object sender, ManagedTaskProgressItem progressItem)
        {
            Assert.Equal(progressItem.StepName, "step: " + progressItem.Percentage);
        }

        [Fact]
        void Test_MessagePack()
        {
            var managedTasks = new ManagedTasks.ManagedTasks();


            managedTasks.OnProgress += Progress;
            
            var progressTask = new ProgressTask(20, 5);
            
            var task1 = managedTasks.Add("123", "task", "test", progressTask, null);
            var serialized = MessagePackSerializer.Serialize(task1);
            var newTask = MessagePackSerializer.Deserialize<ManagedTask>(serialized);
            
            Assert.Equal(task1.OriginatorId, newTask.OriginatorId);
        }
        
        [Fact]
        void Test_NewtonSoftJson()
        {
            var managedTasks = new ManagedTasks.ManagedTasks();


            managedTasks.OnProgress += Progress;
            
            var progressTask = new ProgressTask(20, 5);
            
            var task1 = managedTasks.Add("123", "task", "test", progressTask, null);
            var serialized = JsonConvert.SerializeObject(task1);
            var newTask = JsonConvert.DeserializeObject<ManagedTask>(serialized);
            
            Assert.Equal(task1.OriginatorId, newTask.OriginatorId);
        }
        
        [Fact]
        void TestTxtJson()
        {
            var managedTasks = new ManagedTasks.ManagedTasks();


            managedTasks.OnProgress += Progress;
            
            var progressTask = new ProgressTask(20, 5);
            
            var task1 = managedTasks.Add("123", "task", "test", progressTask, null);
            var serialized = System.Text.Json.JsonSerializer.Serialize(task1);
            var newTask = System.Text.Json.JsonSerializer.Deserialize<ManagedTask>(serialized);
            
            Assert.Equal(task1.OriginatorId, newTask.OriginatorId);
        }

    }
}