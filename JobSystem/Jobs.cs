

public static Program instance;                        //make eg. Echo accessible anywhere in the code
public static bool autoOptimizeRuntime = true;         //will begin override const_maxTimeUsagePercent based on runtime-stats of each Job.


public Program(){
  instance = this;
	Runtime.UpdateFrequency = UpdateFrequency.Update1;   //run Main(): 60x/sec
	
  JobManager.put(new Job(new Job01(), new Object[0])); //register a new example Job with 0 input values.
}
public void Save(){                                    //last function called befor the game stops running the script

}

public void Main(string argument, UpdateType updateSource){
	JobManager.continueJobs();                           //runns the Job Scheduler
}

//------------------------------------Sheduler------------------------------------

public interface IJob {                                //Your custom Job needs to extend this Interface and a implementation of run() 
	void run(Object[] input);                            //  where input is a list of the stuff you want to inizialize it with.
}

public class Job {                                     //A Job-Instance is the Container of all-you-want-to-do:
	private IJob action;                                 // the action aka. tghe target Class you want to execute,
	private Object[] initData;                           // initData is just the content to start run() with,
	private long wakeupTimeStamp;                        // and wakeupTimeStamp is internally used to manage a delayed start.
  
	public Job(IJob action, Object[] initData){          //here you are create your Job bundle.
		this.action = action;
		this.initData = initData;
	}
	public void run(){                                   //the Scheduler will call this Method that finally calls your code.
		action.run(initData);
	}
	
	public IJob getAction(){
		return action;
	}
	
	public void setWakeupTimeStamp(long t){
		wakeupTimeStamp = t;
	}
	
	public long getWakeupTimeStamp(){
		return wakeupTimeStamp;
	}
}

public class JobManager{
	private static int const_stats_record_size = 10;     //here you can set the count of samples that will contain time cost. as more as you use, as better it should prevent for too-much-time-consumption.
	private static double const_maxTimeUsagePercent = 0.5;//if autoOptimizeRuntime above is false, this value declares, at with percent of used progam runtime it will interrupt and return to await the next cycle.
	
	private static List<Job> jobList = new List<Job>();
	private static List<Job> sheduledJobList = new List<Job>();
	private static Dictionary<Type, int[]> statsMap = new Dictionary<Type, int[]>();
	private static long processLoopTimeCounter = 0;
	
	private static List<Type> jobLockList = new List<Type>();
	
	private static int offset = 0;
	
	private static int mostTimeConsumingJobVal=0;        //used for debuging, you can see in the log, witch Job runnes the most of the time and how long.
	private static String mostTimeConsumingJobName="";   //to keep the system running in mode autoOptimizeRuntime = true,
                                                       //  it will constantly decrement because otherwise it will slow down the whole execution system.
	public static void put(Job j){                       //here you can insert all your Jobs.
		jobList.Add(j);
	}
	
	public static void putFirst(Job j){                  //if it is required to push your Job at first position you can do it here, but it is most of the time useless.
		jobList.Insert(0, j);                              //if you realy want to await for a Job-type befor launching another one, check with isWaiting() if your job is still waiting.
	}
	
	public static void putSheduled(Job j, long skipFrames){//here you can additionally put a number, witch discribes how many cycles the Job must wait until it will inserted into the normal Job queue. This can be quite handy to slow down specific funtions.
		j.setWakeupTimeStamp(processLoopTimeCounter + skipFrames);
		sheduledJobList.Add(j);
	}
	
	public static void continueJobs(){                   //should be called in the Main() of your script.
		//updateWaitingTypeCache();
		
		processLoopTimeCounter++;
		jobLockList.Clear();
		for(int i=sheduledJobList.Count() - 1; i>=0; i--){
			Job nextJob = sheduledJobList[i];
			if(processLoopTimeCounter > nextJob.getWakeupTimeStamp()){//if there is a Job that dont need to wait another cycle it will be inserted here into the normal Job-queue.
				sheduledJobList.Remove(nextJob);
				//jobList.Add(nextJob);
				putFirst(nextJob);
			}
		}
		if(sheduledJobList.Count() == 0){
			processLoopTimeCounter = 0;
		}
		int doneJobs = 1;//if 0, security is disabled (if a process is running for longer than 50% of CPU time, it will get blocked; else 1+)
		while(true){
			if(jobList.Count() <= offset) {                  //if the pointer is out-of-bounds: reset it.
				offset = 0;
				//instance.Echo("return");
				//return;
			}
			if(jobList.Count() == 0) return;                 //if there are no more Jobs: exit.
			Job nextJob = jobList[offset];                   //offset is introduced to bring more kind-of 'randomized' execution in the system, but on the simplest way possible.
			IMyGridProgramRuntimeInfo rtData = instance.Runtime;
			offset++;                                        //the canche that the code is getting stuck is much slower, but since autoOptimizeRuntime = true maybe useless, that may get changed in future versions.
			if(jobLockList.Contains(nextJob.getAction().GetType())){//experimental function!
				double a = ((double)rtData.CurrentInstructionCount / (double)rtData.MaxInstructionCount);
				if(a >= const_maxTimeUsagePercent) {           //generic question: is there enough time left?
					instance.Echo("interrupt, " + a + "%,\n" + jobList.Count() + " Jobs left,\n" + offset + " Jobs Skipped.");
					if(jobList.Count() == 1 && offset == 1){     //debug for the case that a single job has a peak of run-time and it got stuck in te sheduler. If your Methods are small enough, this schould never happen :)
						instance.Echo("Skipped Job Class is " + nextJob.getAction().GetType());
					}
					return;
				}
				continue;
			}
			int relCost = calcRelativeCost(nextJob.getAction());
			double percent_time_used = ((double)(rtData.CurrentInstructionCount + relCost) / (double)rtData.MaxInstructionCount);
			if(percent_time_used >= const_maxTimeUsagePercent && doneJobs > 0) {
				instance.Echo("interrupt, " + percent_time_used + "%,\n" + jobList.Count() + " Jobs left,\n" + offset + " Jobs Skipped.");
				if(jobList.Count() == 1 && offset == 1){
					instance.Echo("Skipped Job Class is " + nextJob.getAction().GetType());
				}
				double mostTimePercent = ((double)mostTimeConsumingJobVal / (double)rtData.MaxInstructionCount);
				instance.Echo("\nmostTimeConsumingJob(" + mostTimePercent + "%):\n" + mostTimeConsumingJobName);
				
				if(autoOptimizeRuntime){                       //if set, the maximum run-time-percent is 100% - the longest task-time *2. "*2" because that the system never trys to reach to perfect 100% because the execution time of your code may variing. BUT it may can fail-calculate/-'predict'. you have been warned :)
					const_maxTimeUsagePercent = 1.0 - (mostTimePercent*2.0);
				}
				
				mostTimeConsumingJobVal -= 10;                 //here the value will slowly decrement.(-=10 vs. rtData.MaxInstructionCount==50 000 should be fast enough for up to 60 calls/sec.
				
				offset = 0;
				return;
			}
			jobList.Remove(nextJob);                         //if there are no more aruments to interrupt, lets execute some tasks!
			int currentCount = rtData.CurrentInstructionCount;//mesure current time.
			nextJob.run();                                   //run your code
			currentCount = rtData.CurrentInstructionCount - currentCount;//mesure current time and calc difference.
			doneJobs++;
			addCostValue(nextJob.getAction(), currentCount); //update cost mapping for your job.
			if(currentCount > mostTimeConsumingJobVal){      //if this class is breaking the current highscore, update these data.
				mostTimeConsumingJobVal = currentCount;
				mostTimeConsumingJobName = nextJob.getAction().GetType().ToString().Replace('+', '.');//i am coming form Java and there a no '+' for subclasses xD its confusing me
			}
		}
	}

	public static void interrupt(IJob j){                //experimental an d NOT good tested right now.
		jobLockList.Add(j.GetType());
	}
	
	public static bool isWaiting(Type t){                //aks the system if a specific class is in queue.
		int timeout = 100;                                 //this feature was added, because at about 10k Jobs in the List (and that can be the case faster than you think ^^),
		foreach(Job j in jobList){                         //  he time consumption of only this method is exploding; currently i dont know a smarter/faster way
			if(j.getAction().GetType() == t) return true;    //  to scan for explicit classes, so if it is out-of-range it just returns a quick true, so the requesting program
			if(timeout-- <= 0) return true;                  //  can decide to wait a bit longer until the jobs decraced to less than in this case 100.
		}                                                  //  if i found a better way it will getting updated.
		return false;
	}
	
	private static int calcRelativeCost(IJob j){
		if(!statsMap.ContainsKey(j.GetType())){
			return 0;
		}
		int[] a = statsMap[j.GetType()];
		int allValues = 0;
		int validCount = 0;
		for(int i=1; i<a.Length; i++) {
			if(a[i] != -1) {
				allValues += a[i];
				validCount++;
			}
		}
		if(validCount == 0) return 0;
		return allValues / validCount;
	}
	
	private static int addCostValue(IJob j, int newParam){
		int[] a;
		if(!statsMap.ContainsKey(j.GetType())){
			a = new int[const_stats_record_size + 1];
			for(int i=1; i<a.Length; i++) a[i] = -1;
			statsMap.Add(j.GetType(), a);
		} else {
			a = statsMap[j.GetType()];
		}
		int p = a[0] + 1;
		if(p >= a.Length) p = 1;
		a[p] = newParam;
		a[0] = p;
		int b = 0;
		for(int i=0; i<p; i++){
			b += a[i];
		}
		return b;
	}
}

//------------------------------------Your Code------------------------------------

public class Job01 : IJob {
	public void run(Object[] input){
		//your code here.
	}
}
